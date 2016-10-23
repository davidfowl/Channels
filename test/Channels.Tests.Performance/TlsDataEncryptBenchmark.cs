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
        private static ISecureChannel _openSslClient;
        private static ISecureChannel _openSslServer;

        [Setup]
        public static void Setup()
        {
            var rnd = new Random(7777);
            dataToSend = new byte[testBytesSize];
            rnd.NextBytes(dataToSend);
            factory = new ChannelFactory();
            _openSslServerContext = new OpenSslSecurityContext(factory, "test", true, _certificatePath, _certificatePassword);
            _openSslClientContext = new OpenSslSecurityContext(factory, "test", false, null, null);
            var loopback = new LoopbackChannel(factory);
            _openSslServer = _openSslServerContext.CreateSecureChannel(loopback.ServerChannel);
            _openSslClient = _openSslClientContext.CreateSecureChannel(loopback.ClientChannel);
            _openSslClient.HandShakeAsync().Wait();
            var outputBuffer = _openSslClient.Output.Alloc();
            outputBuffer.Write(dataToSend);
            outputBuffer.FlushAsync().Wait();
        }

        public static void SslStreamOverChannels()
        {
            using (var fact = new ChannelFactory())
            using (var cert = new X509Certificate(_certificatePath, _certificatePassword))
            {
                var loopback = new LoopbackChannel(fact);
                using (var serverStream = new SslStream(loopback.ServerChannel.GetStream(), false))
                using (var clientStream = new SslStream(loopback.ClientChannel.GetStream(), false, ValidateServerCertificate, null, EncryptionPolicy.RequireEncryption))
                {
                    Task[] handShakes = new Task[2];
                    handShakes[1] = clientStream.AuthenticateAsClientAsync("CARoot");
                    handShakes[0] = serverStream.AuthenticateAsServerAsync(cert, false, System.Security.Authentication.SslProtocols.Tls, false);

                    Task.WaitAll(handShakes);

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

        [Benchmark(OperationsPerInvoke = InnerLoopCount * 4)]
        public static void OpenSslChannelAllTheThings()
        {
            for (int i = 0; i < InnerLoopCount; i++)
            {
                while (true)
                {
                    var result = _openSslServer.Input.ReadAsync().GetResult();
                    if (result.Buffer.Length == dataToSend.Length)
                    {
                        //got the lot, send it back
                        var outbuffer = _openSslServer.Output.Alloc();
                        outbuffer.Append(result.Buffer);
                        outbuffer.FlushAsync().Wait();
                        _openSslServer.Input.Advance(result.Buffer.End, result.Buffer.End);
                        break;
                    }
                    _openSslServer.Input.Advance(result.Buffer.Start, result.Buffer.End);
                    continue;
                }
                while (true)
                {
                    var result = _openSslClient.Input.ReadAsync().GetResult();
                    if (result.Buffer.Length == dataToSend.Length)
                    {
                        //got the lot, send it back
                        var outbuffer = _openSslClient.Output.Alloc();
                        outbuffer.Append(result.Buffer);
                        outbuffer.FlushAsync().Wait();
                        _openSslClient.Input.Advance(result.Buffer.End, result.Buffer.End);
                        break;
                    }
                    _openSslClient.Input.Advance(result.Buffer.Start, result.Buffer.End);
                    continue;
                }
            }
        }

        public static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
    }
}
