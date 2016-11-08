using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Channels.Networking.TLS;
using Channels.Text.Primitives;
using Microsoft.Extensions.PlatformAbstractions;

namespace Channels.Tests.Performance
{
    [Config(typeof(LargerTestConfig))]
    public class TlsDataEncryptBenchmark
    {
        static byte[] dataToSend;
        private static int testBytesSize = 1024;
        public const int InnerLoopCount = 10000;
        private static readonly string _certificatePath = System.IO.Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "data", "TestCert.pfx");
        private static readonly string _certificatePassword = "Test123t";
        private static ChannelFactory factory;
        private static OpenSslSecurityContext _openSslServerContext;
        private static OpenSslSecurityContext _openSslClientContext;
        private static X509Certificate _cert = new X509Certificate(_certificatePath, _certificatePassword);

        [Setup]
        public static void Setup()
        {
            var rnd = new Random(7777);
            dataToSend = new byte[testBytesSize];
            rnd.NextBytes(dataToSend);
            factory = new ChannelFactory();
            _openSslServerContext = new OpenSslSecurityContext(factory, "test", true, _certificatePath, _certificatePassword);
            _openSslClientContext = new OpenSslSecurityContext(factory, "test", false, null, null);
        }
        [Benchmark(Baseline = true)]
        public static void SslStreamOverChannels()
        {
            var loopback = new LoopbackChannel(factory);
            using (var serverStream = new SslStream(loopback.ServerChannel.GetStream(), false))
            using (var clientStream = new SslStream(loopback.ClientChannel.GetStream(), false, ValidateServerCertificate, null, EncryptionPolicy.RequireEncryption))
            {
                var t = clientStream.AuthenticateAsClientAsync("CARoot");
                serverStream.AuthenticateAsServer(_cert, false, System.Security.Authentication.SslProtocols.Tls, false);
                t.Wait();
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

        [Benchmark(OperationsPerInvoke = InnerLoopCount * 4)]
        public static void OpenSslChannelAllTheThings()
        {
            var loopback = new LoopbackChannel(factory);
            using (var openSslServer = _openSslServerContext.CreateSecureChannel(loopback.ServerChannel))
            using (var openSslClient = _openSslClientContext.CreateSecureChannel(loopback.ClientChannel))
            {
                openSslClient.HandShakeAsync().Wait();
                openSslServer.HandShakeAsync().Wait();
                for (int i = 0; i < InnerLoopCount; i++)
                {
                    var outputBuffer = openSslServer.Output.Alloc(dataToSend.Length);
                    outputBuffer.Write(dataToSend);
                    outputBuffer.FlushAsync().Wait();
                    int total = dataToSend.Length;
                    var result = openSslClient.Input.ReadAsync().GetResult();
                    while (result.Buffer.Length < total)
                    {
                        openSslClient.Input.Advance(result.Buffer.Start, result.Buffer.End);
                        result = openSslClient.Input.ReadAsync().GetResult();
                    }
                    result.Buffer.CopyTo(dataToSend);
                    openSslClient.Input.Advance(result.Buffer.End,result.Buffer.End);
                }
            }
        }

        public static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
    }
}
