using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.PlatformAbstractions;

namespace Channels.Tests.Performance
{
    public class TlsDataEncryptBenchmark
    {
        static byte[] dataToSend;
        public const int InnerLoopCount = 100;
        private static readonly string _certificatePath = System.IO.Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "data", "TestCert.pfx");
        private static readonly string _certificatePassword = "Test123t";

        [Setup]
        public static void Setup()
        {
            var rnd = new Random(7777);
            dataToSend = new byte[2048];
            rnd.NextBytes(dataToSend);
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public static void SslStreamOverChannels()
        {
            using (var fact = new ChannelFactory())
            using (var cert = new X509Certificate(_certificatePath, _certificatePassword))
            {
                var loopback = new LoopbackChannel(fact);
                using (var serverStream = new SslStream(loopback.ServerChannel.GetStream(), false))
                using (var clientStream = new SslStream(loopback.ClientChannel.GetStream(), false, ValidateServerCertificate, null, EncryptionPolicy.RequireEncryption))
                {
                    serverStream.AuthenticateAsServerAsync(cert, false, System.Security.Authentication.SslProtocols.Tls, false);
                    clientStream.AuthenticateAsClient("CARoot");

                    for (int i = 0; i < InnerLoopCount; i++)
                    {
                        serverStream.Write(dataToSend);
                        int total = dataToSend.Length;
                        while (total > 0)
                        {
                            total -= clientStream.Read(dataToSend, dataToSend.Length - total, total);
                        }
                    }
                }
            }
        }

        public static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None || sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            {
                return true;
            }
            return false;
        }
    }
}
