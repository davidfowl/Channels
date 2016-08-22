// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Channels.Samples.Internal.Winsock
{
    public sealed class RegisteredIO
    {
        public RioRegisterBuffer RioRegisterBuffer;

        public RioCreateCompletionQueue RioCreateCompletionQueue;
        public RioCreateRequestQueue RioCreateRequestQueue;


        public RioReceive RioReceive;
        public RioSend Send;

        public RioNotify Notify;

        public RioCloseCompletionQueue CloseCompletionQueue;
        public RioDequeueCompletion DequeueCompletion;
        public RioDeregisterBuffer DeregisterBuffer;
        public RioResizeCompletionQueue ResizeCompletionQueue;
        public RioResizeRequestQueue ResizeRequestQueue;


        public const long CachedValue = long.MinValue;

        public RegisteredIO()
        {
        }
    }
}