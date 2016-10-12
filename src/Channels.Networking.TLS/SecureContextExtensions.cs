using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Channels.Networking.TLS.Internal;

namespace Channels.Networking.TLS
{
    internal static class SecureContextExtensions
    {
        /// <summary>
        /// Encrypts by allocating a single block on the out buffer to contain the message, plus the trailer and header. Then uses SSPI to write directly onto the output
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="context">The secure context that holds the information about the current connection</param>
        /// <param name="encryptedData">The buffer to write the encryption results to</param>
        /// <param name="plainText">The buffer that will provide the bytes to be encrypted</param>
        internal static unsafe SecurityStatus Encrypt<T>(this T context, ReadableBuffer unencrypted, WritableBuffer encryptedData) where T : ISecureContext
        {
            encryptedData.Ensure(context.TrailerSize + context.HeaderSize + unencrypted.Length);
            void* outBufferPointer;
            encryptedData.Memory.TryGetPointer(out outBufferPointer);

            //Copy the unencrypted across to the encrypted channel, it will be updated in place and destroyed
            unencrypted.CopyTo(encryptedData.Memory.Slice(context.HeaderSize, unencrypted.Length));

            var securityBuff = stackalloc SecurityBuffer[4];
            SecurityBufferDescriptor sdcInOut = new SecurityBufferDescriptor(4);
            securityBuff[0] = new SecurityBuffer(outBufferPointer, context.HeaderSize, SecurityBufferType.Header);
            securityBuff[1] = new SecurityBuffer((byte*)outBufferPointer + context.HeaderSize, unencrypted.Length, SecurityBufferType.Data);
            securityBuff[2] = new SecurityBuffer((byte*)securityBuff[1].tokenPointer + unencrypted.Length, context.TrailerSize, SecurityBufferType.Trailer);

            sdcInOut.UnmanagedPointer = securityBuff;

            var handle = context.ContextHandle;
            var result = (SecurityStatus)InteropSspi.EncryptMessage(ref handle, 0, sdcInOut, 0);
            if (result == 0)
            {
                var totalSize = securityBuff[0].size + securityBuff[1].size + securityBuff[2].size;
                encryptedData.Advance(totalSize);
                return result;
            }
            else
            {
                //Zero out the output buffer before throwing the exception to stop any data being sent in the clear
                //By a misbehaving underlying channel
                var memoryToClear = new Span<byte>(outBufferPointer, context.HeaderSize + context.TrailerSize + unencrypted.Length);
                if (context.HeaderSize + context.TrailerSize + unencrypted.Length > SecurityContext.MaxStackAllocSize)
                {
                    var empty = new byte[context.HeaderSize + context.TrailerSize + unencrypted.Length];
                    memoryToClear.Set(empty);
                }
                else
                {
                    var empty = stackalloc byte[context.HeaderSize + context.TrailerSize + unencrypted.Length];
                    memoryToClear.Set(empty, context.HeaderSize + context.TrailerSize + unencrypted.Length);
                }
                throw new InvalidOperationException($"There was an issue encrypting the data {result}");
            }
        }

        /// <summary>
        /// Decrypts the data that comes from a readable buffer. If it is in a single span it will be decrypted in place. Next we will attempt to use the stack. If it is
        /// too big for that we will allocate.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="context">The secure context that holds the information about the current connection</param>
        /// <param name="encryptedData">The buffer to write the encryption results to</param>
        /// <param name="decryptedData">The buffer that will provide the bytes to be encrypted</param>
        /// <returns></returns>
        internal static unsafe SecurityStatus Decrypt<T>(this T context, ReadableBuffer encryptedData, WritableBuffer decryptedData) where T : ISecureContext
        {
            GCHandle handle = default(GCHandle);
            try
            {
                void* pointer;
                if (encryptedData.IsSingleSpan)
                {
                    encryptedData.First.TryGetPointer(out pointer);
                }
                else
                {
                    if (encryptedData.Length <= SecurityContext.MaxStackAllocSize)
                    {
                        var tmpBuffer = stackalloc byte[encryptedData.Length];
                        encryptedData.CopyTo(new Span<byte>(tmpBuffer, encryptedData.Length));
                        pointer = tmpBuffer;
                    }
                    else
                    {
                        var tmpBuffer = new byte[encryptedData.Length];
                        encryptedData.CopyTo(tmpBuffer);
                        handle = GCHandle.Alloc(encryptedData, GCHandleType.Pinned);
                        pointer = (void*)handle.AddrOfPinnedObject();
                    }
                }
                int offset = 0;
                int count = encryptedData.Length;

                var secStatus = DecryptMessage(pointer, ref offset, ref count, context.ContextHandle);
                if (encryptedData.IsSingleSpan)
                {
                    //The data was always in a single continous buffer so we can just append the decrypted data to the output
                    encryptedData = encryptedData.Slice(offset, count);
                    decryptedData.Append(encryptedData);
                }
                else
                {
                    //The data was multispan so we had to copy it out into either a stack pointer or an allocated and pinned array
                    //so now we need to copy it out to the output
                    decryptedData.Write(new Span<byte>(pointer, encryptedData.Length));
                }
                return secStatus;
            }
            finally
            {
                if (handle.IsAllocated) { handle.Free(); }
            }
        }

        private static unsafe SecurityStatus DecryptMessage(void* buffer, ref int offset, ref int count, SSPIHandle context)
        {
            var securityBuff = stackalloc SecurityBuffer[4];
            SecurityBufferDescriptor sdcInOut = new SecurityBufferDescriptor(4);
            securityBuff[0] = new SecurityBuffer(buffer, count, SecurityBufferType.Data);
            sdcInOut.UnmanagedPointer = securityBuff;

            var errorCode = (SecurityStatus)InteropSspi.DecryptMessage(ref context, sdcInOut, 0, null);

            if (errorCode == 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (securityBuff[i].type == SecurityBufferType.Data)
                    {
                        //we have found the data lets find the offset
                        offset = (int)((byte*)securityBuff[i].tokenPointer - (byte*)buffer);
                        if (offset > (count - 1))
                        {
                            throw new OverflowException();
                        }
                        count = securityBuff[i].size;
                        return errorCode;
                    }
                }
            }
            throw new InvalidOperationException($"There was an error ncrypting the data {errorCode}");
        }

        /// <summary>
        /// Checks to see if we have enough data for a frame and if the basic frame header is valid.
        /// </summary>
        /// <param name="buffer">The input buffer, it will be returned with the frame sliced out if there is a complete frame found</param>
        /// <param name="messageBuffer">If a frame is found this contains that frame</param>
        /// <returns>The status of the check for frame</returns>
        internal static bool TryGetFrameType(ref ReadableBuffer buffer, out ReadableBuffer messageBuffer, out TlsFrameType frameType)
        {
            frameType = TlsFrameType.Incomplete;
            //Need at least 5 bytes to be useful
            if (buffer.Length < 5)
            {
                messageBuffer = default(ReadableBuffer);
                return false;
            }
            frameType = (TlsFrameType)buffer.ReadBigEndian<byte>();

            //Check it's a valid frametype for what we are expecting
            if (frameType != TlsFrameType.AppData && frameType != TlsFrameType.Alert
                && frameType != TlsFrameType.ChangeCipherSpec && frameType != TlsFrameType.Handshake)
            {
                throw new FormatException($"The tls frame type was invalid value was {frameType}");
            }
            //now we get the version
            var version = buffer.Slice(1).ReadBigEndian<ushort>();

            if (version < 0x300 || version >= 0x500)
            {
                messageBuffer = default(ReadableBuffer);
                Debugger.Break();
                throw new FormatException($"The tls frame type was invalid due to the version value was {frameType}");
            }
            var length = buffer.Slice(3).ReadBigEndian<ushort>();
            // If we have a full frame slice it out and move the original buffer forward
            if (buffer.Length >= (length + 5))
            {
                messageBuffer = buffer.Slice(0, length + 5);
                buffer = buffer.Slice(messageBuffer.End);
                return true;
            }
            messageBuffer = default(ReadableBuffer);
            return false;
        }
    }
}
