using System;
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

            unencrypted.CopyTo(encryptedData.Memory.Slice(context.HeaderSize, unencrypted.Length));

            var securityBuff = stackalloc SecurityBuffer[4];
            SecurityBufferDescriptor sdcInOut = new SecurityBufferDescriptor(4);
            securityBuff[0].size = context.HeaderSize;
            securityBuff[0].type = SecurityBufferType.Header;
            securityBuff[0].tokenPointer = outBufferPointer;

            securityBuff[1].size = unencrypted.Length;
            securityBuff[1].type = SecurityBufferType.Data;
            securityBuff[1].tokenPointer = (byte*)outBufferPointer + context.HeaderSize;

            securityBuff[2].size = context.TrailerSize;
            securityBuff[2].type = SecurityBufferType.Trailer;
            securityBuff[2].tokenPointer = (byte*)outBufferPointer + context.HeaderSize + unencrypted.Length;

            securityBuff[3].size = 0;
            securityBuff[3].tokenPointer = null;
            securityBuff[3].type = SecurityBufferType.Empty;

            sdcInOut.UnmanagedPointer = securityBuff;

            var handle = context.ContextHandle;
            var result = (SecurityStatus)InteropSspi.EncryptMessage(ref handle, 0, sdcInOut, 0);
            if (result == 0)
            {
                encryptedData.Advance(context.HeaderSize + context.TrailerSize + unencrypted.Length);
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
                        throw new OverflowException($"We need to create a buffer on the stack of size {encryptedData.Length} but the max is {SecurityContext.MaxStackAllocSize}");
                    }
                }
                int offset = 0;
                int count = encryptedData.Length;

                var secStatus = DecryptMessage(pointer, ref offset, ref count, context.ContextHandle);
                if (encryptedData.IsSingleSpan)
                {
                    encryptedData = encryptedData.Slice(offset, count);
                    decryptedData.Append(ref encryptedData);
                }
                else
                {
                    decryptedData.Ensure(encryptedData.Length);
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
            securityBuff[0].size = count;
            securityBuff[0].tokenPointer = buffer;
            securityBuff[0].type = SecurityBufferType.Data;
            securityBuff[1].size = 0;
            securityBuff[1].tokenPointer = null;
            securityBuff[1].type = SecurityBufferType.Empty;
            securityBuff[2].size = 0;
            securityBuff[2].tokenPointer = null;
            securityBuff[2].type = SecurityBufferType.Empty;
            securityBuff[3].size = 0;
            securityBuff[3].tokenPointer = null;
            securityBuff[3].type = SecurityBufferType.Empty;

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
        internal static TlsFrameType CheckForFrameType(ref ReadableBuffer buffer, out ReadableBuffer messageBuffer)
        {
            //Need at least 5 bytes to be useful
            if (buffer.Length < 5)
            {
                messageBuffer = default(ReadableBuffer);
                return TlsFrameType.Incomplete;
            }
            var messageType = (TlsFrameType)buffer.ReadBigEndian<byte>();

            //Check it's a valid frametype for what we are expecting
            if (messageType != TlsFrameType.AppData && messageType != TlsFrameType.Alert
                && messageType != TlsFrameType.ChangeCipherSpec && messageType != TlsFrameType.Handshake)
            {
                messageBuffer = default(ReadableBuffer);
                return TlsFrameType.Invalid;
            }
            //now we get the version
            var version = buffer.Slice(1).ReadBigEndian<ushort>();

            if (version < 0x300 || version >= 0x500)
            {
                messageBuffer = default(ReadableBuffer);
                return TlsFrameType.Invalid;
            }
            var length = buffer.Slice(3).ReadBigEndian<ushort>();

            if (buffer.Length >= length)
            {
                messageBuffer = buffer.Slice(0, length + 5);
                buffer = buffer.Slice(messageBuffer.End);
                return messageType;
            }
            messageBuffer = default(ReadableBuffer);
            return TlsFrameType.Incomplete;
        }
    }
}
