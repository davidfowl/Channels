using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Channels.Networking.TLS.Internal;

namespace Channels.Networking.TLS
{
    internal unsafe class SecureConnectionContext : ISecureContext
    {
        private SecurityContext _securityContext;
        private SSPIHandle _contextPointer;
        private int _headerSize = 5; //5 is the minimum (1 for frame type, 2 for version, 2 for frame size)
        private int _trailerSize = 16;
        private int _maxDataSize = 16354;
        private bool _readyToSend;
        private ApplicationProtocols.ProtocolIds _negotiatedProtocol;

        public SecureConnectionContext(SecurityContext securityContext)
        {
            _securityContext = securityContext;
        }

        public bool ReadyToSend => _readyToSend;
        public ApplicationProtocols.ProtocolIds NegotiatedProtocol => _negotiatedProtocol;
        public int HeaderSize { get { return _headerSize; } set { _headerSize = value; } }
        public int TrailerSize { get { return _trailerSize; } set { _trailerSize = value; } }
        public SSPIHandle ContextHandle => _contextPointer;
        
        /// <summary>
        /// Without a payload from the client the server will just return straight away.
        /// </summary>
        /// <param name="writeBuffer"></param>
        public void ProcessContextMessage(WritableBuffer writeBuffer)
        {
            if (_securityContext.IsServer)
            {
                return;
            }
            ProcessContextMessage(default(ReadableBuffer), writeBuffer);
        }

        /// <summary>
        /// Processes the tokens or cipher change messages from a client and can then return server messages for the client
        /// </summary>
        /// <param name="readBuffer"></param>
        /// <param name="writeBuffer"></param>
        public void ProcessContextMessage(ReadableBuffer readBuffer, WritableBuffer writeBuffer)
        {
            var handleForAllocation = default(GCHandle);
            try
            {
                var output = new SecurityBufferDescriptor(2);
                var outputBuff = stackalloc SecurityBuffer[2];
                outputBuff[0] = new SecurityBuffer(null, 0, SecurityBufferType.Token);
                outputBuff[1] = new SecurityBuffer(null, 0, SecurityBufferType.Alert);
                output.UnmanagedPointer = outputBuff;

                var handle = _securityContext.CredentialsHandle;
                SSPIHandle localhandle = _contextPointer;
                void* contextptr;
                void* newContextptr;
                if (_contextPointer.handleHi == IntPtr.Zero && _contextPointer.handleLo == IntPtr.Zero)
                {
                    contextptr = null;
                    newContextptr = &localhandle;
                }
                else
                {
                    contextptr = &localhandle;
                    newContextptr = null;
                }
                var unusedAttributes = default(ContextFlags);
                SecurityBufferDescriptor* pointerToDescriptor = null;

                if (readBuffer.Length > 0)
                {
                    var input = new SecurityBufferDescriptor(2);
                    var inputBuff = stackalloc SecurityBuffer[2];
                    inputBuff[0].size = readBuffer.Length;
                    inputBuff[0].type = SecurityBufferType.Token;

                    if (readBuffer.IsSingleSpan)
                    {
                        void* arrayPointer;
                        readBuffer.First.TryGetPointer(out arrayPointer);
                        inputBuff[0].tokenPointer = arrayPointer;
                    }
                    else
                    {
                        if (readBuffer.Length <= SecurityContext.MaxStackAllocSize)
                        {

                            var tempBuffer = stackalloc byte[readBuffer.Length];
                            var tmpSpan = new Span<byte>(tempBuffer, readBuffer.Length);
                            readBuffer.CopyTo(tmpSpan);
                            inputBuff[0].tokenPointer = tempBuffer;
                        }
                        else
                        {
                            //We have to allocate... sorry
                            var tempBuffer = new byte[readBuffer.Length];
                            var tmpSpan = new Span<byte>(tempBuffer);
                            readBuffer.CopyTo(tmpSpan);
                            handleForAllocation = GCHandle.Alloc(tempBuffer, GCHandleType.Pinned);
                            inputBuff[0].tokenPointer = (void*)handleForAllocation.AddrOfPinnedObject();
                        }
                    }
                    //If we have APLN extensions to send use the last buffer
                    if(_securityContext.AplnRequired)
                    {
                        inputBuff[1] = _securityContext.AplnBuffer;
                    }
                    input.UnmanagedPointer = inputBuff;
                    pointerToDescriptor = &input;
                }
                else
                {
                    //Only build an input buffer if we have to send APLN extensions
                    if (_securityContext.AplnRequired)
                    {
                        var input = new SecurityBufferDescriptor(1);
                        var inputBuff = stackalloc SecurityBuffer[1];
                        inputBuff[0] = _securityContext.AplnBuffer;
                        input.UnmanagedPointer = inputBuff;
                        pointerToDescriptor = &input;
                    }
                }
                //We call accept security context for a server (as it is initiated by the client) and for the client we call Initialize
                long timestamp = 0;
                SecurityStatus errorCode;
                if (_securityContext.IsServer)
                {
                    errorCode = (SecurityStatus)InteropSspi.AcceptSecurityContext(ref handle, contextptr, pointerToDescriptor, SecurityContext.ServerRequiredFlags, 0, newContextptr, &output, ref unusedAttributes, out timestamp);
                }
                else
                {
                    errorCode = (SecurityStatus)InteropSspi.InitializeSecurityContextW(ref handle, contextptr, _securityContext.HostName, SecurityContext.RequiredFlags | ContextFlags.InitManualCredValidation, 0, Endianness.Native, pointerToDescriptor, 0, newContextptr, &output, ref unusedAttributes, out timestamp);
                }

                _contextPointer = localhandle;
                if (errorCode == SecurityStatus.ContinueNeeded || errorCode == SecurityStatus.OK)
                {
                    if (outputBuff[0].size > 0)
                    {
                        writeBuffer.Write(new Span<byte>(outputBuff[0].tokenPointer, outputBuff[0].size));
                        InteropSspi.FreeContextBuffer((IntPtr)outputBuff[0].tokenPointer);
                    }
                    if (errorCode == SecurityStatus.OK)
                    {
                        ContextStreamSizes ss;
                        //We have a valid context so lets query it for the size of the header and trailer
                        InteropSspi.QueryContextAttributesW(ref _contextPointer, ContextAttribute.StreamSizes, out ss);
                        _headerSize = ss.header;
                        _trailerSize = ss.trailer;
                        //If we needed APLN this should now be set
                        if (_securityContext.AplnRequired)
                        {
                            _negotiatedProtocol = ApplicationProtocols.FindNegotiatedProtocol(_contextPointer);
                        }
                        _readyToSend = true;
                    }
                    return;
                }
                throw new InvalidOperationException($"An error occured trying to negoiate a session {errorCode}");
            }
            finally
            {
                if (handleForAllocation.IsAllocated)
                {
                    handleForAllocation.Free();
                }
            }
        }

        public void Dispose()
        {
            if (_contextPointer.IsValid)
            {
                InteropSspi.DeleteSecurityContext(ref _contextPointer);
                _contextPointer = default(SSPIHandle);
            }
            GC.SuppressFinalize(this);
        }

        ~SecureConnectionContext()
        {
            Dispose();
        }
    }
}
