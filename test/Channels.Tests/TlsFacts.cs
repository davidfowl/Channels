using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Channels.Networking.Libuv;
using Channels.Networking.Sockets;
using Channels.Networking.TLS;
using Channels.Tests.Internal;
using Channels.Text.Primitives;
using Microsoft.Extensions.PlatformAbstractions;
using Xunit;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
namespace Channels.Tests
{
    public class TlsFacts
    {
        private static readonly string _certificatePath = System.IO.Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "data", "TestCert.pfx");
        private static readonly string _certificatePassword = "Test123t";
        private static readonly string _shortTestString = "The quick brown fox jumped over the lazy dog.";

        [WindowsOnlyFact()]
        public async Task SspiAplnMatchingProtocol()
        {
            using (X509Certificate cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var serverContext = new SecurityContext(factory, "CARoot", true, cert, ApplicationProtocols.ProtocolIds.Http11 | ApplicationProtocols.ProtocolIds.Http2overTLS))
            using (var clientContext = new SecurityContext(factory, "CARoot", false, null, ApplicationProtocols.ProtocolIds.Http2overTLS))
            {
                var loopback = new LoopbackChannel(factory);
                using (var server = serverContext.CreateSecureChannel(loopback.ServerChannel))
                using (var client = clientContext.CreateSecureChannel(loopback.ClientChannel))
                {
                    Echo(server);
                    var proto = await client.HandShakeAsync();
                    Assert.Equal(ApplicationProtocols.ProtocolIds.Http2overTLS, proto);
                }
            }
        }

        [WindowsOnlyFact()]
        public async Task OpenSslAsServerSspiAsClientAplnMatchingProtocol()
        {
            using (X509Certificate cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var serverContext = new OpenSslSecurityContext(factory, "test", true, _certificatePath, _certificatePassword, ApplicationProtocols.ProtocolIds.Http11 | ApplicationProtocols.ProtocolIds.Http2overTLS))
            using (var clientContext = new SecurityContext(factory, "CARoot", false, cert, ApplicationProtocols.ProtocolIds.Http2overTLS))
            {
                var loopback = new LoopbackChannel(factory);
                using (var server = serverContext.CreateSecureChannel(loopback.ServerChannel))
                using (var client = clientContext.CreateSecureChannel(loopback.ClientChannel))
                {
                    Echo(server);
                    var proto = await client.HandShakeAsync();
                    Assert.Equal(ApplicationProtocols.ProtocolIds.Http2overTLS, proto);
                }
            }
        }

        [WindowsOnlyFact()]
        public async Task SspiAsServerOpenSslAsClientAplnMatchingProtocol()
        {
            using (X509Certificate cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var clientContext = new OpenSslSecurityContext(factory, "test", false, _certificatePath, _certificatePassword, ApplicationProtocols.ProtocolIds.Http11 | ApplicationProtocols.ProtocolIds.Http2overTLS))
            using (var serverContext = new SecurityContext(factory, "CARoot", true, cert, ApplicationProtocols.ProtocolIds.Http2overTLS))
            {
                var loopback = new LoopbackChannel(factory);
                Echo(serverContext.CreateSecureChannel(loopback.ServerChannel));
                var client = clientContext.CreateSecureChannel(loopback.ClientChannel);
                var proto = await client.HandShakeAsync();
                Assert.Equal(ApplicationProtocols.ProtocolIds.Http2overTLS, proto);
            }
        }

        [WindowsOnlyFact]
        public async Task OpenSslAndSspiChannelAllTheThings()
        {
            using (X509Certificate cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var serverContext = new OpenSslSecurityContext(factory, "test", true, _certificatePath, _certificatePassword))
            using (var clientContext = new SecurityContext(factory, "CARoot", false, null))
            {
                var loopback = new LoopbackChannel(factory);
                Echo(serverContext.CreateSecureChannel(loopback.ServerChannel));
                var client = clientContext.CreateSecureChannel(loopback.ClientChannel);
                var outputBuffer = client.Output.Alloc();
                outputBuffer.WriteUtf8String(_shortTestString);
                await outputBuffer.FlushAsync();

                //Now check we get the same thing back
                string resultString;
                while (true)
                {
                    var result = await client.Input.ReadAsync();
                    if (result.Buffer.Length >= _shortTestString.Length)
                    {
                        resultString = result.Buffer.GetUtf8String();
                        client.Input.Advance(result.Buffer.End);
                        break;
                    }
                    client.Input.Advance(result.Buffer.Start, result.Buffer.End);
                }
                Assert.Equal(_shortTestString, resultString);
            }
        }

        [Fact]
        public async Task OpenSslChannelAllTheThings()
        {
            using (X509Certificate cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var serverContext = new OpenSslSecurityContext(factory, "test", true, _certificatePath, _certificatePassword))
            using (var clientContext = new OpenSslSecurityContext(factory, "test", false, _certificatePath, _certificatePassword))
            {
                var loopback = new LoopbackChannel(factory);
                using (var server = serverContext.CreateSecureChannel(loopback.ServerChannel))
                using (var client = clientContext.CreateSecureChannel(loopback.ClientChannel))
                {
                    Echo(server);
                    await client.HandShakeAsync();
                    var outputBuffer = client.Output.Alloc();
                    outputBuffer.WriteUtf8String(_shortTestString);
                    await outputBuffer.FlushAsync();

                    //Now check we get the same thing back
                    string resultString;
                    while (true)
                    {
                        var result = await client.Input.ReadAsync();
                        if (result.Buffer.Length >= _shortTestString.Length)
                        {
                            resultString = result.Buffer.GetUtf8String();
                            client.Input.Advance(result.Buffer.End);
                            break;
                        }
                        client.Input.Advance(result.Buffer.Start, result.Buffer.End);
                    }
                    Assert.Equal(_shortTestString, resultString);
                }
            }
        }

        [WindowsOnlyFact]
        public async Task SspiChannelAllThings()
        {
            using (X509Certificate cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var serverContext = new SecurityContext(factory, "CARoot", true, cert))
            using (var clientContext = new SecurityContext(factory, "CARoot", false, null))
            {
                var loopback = new LoopbackChannel(factory);
                using (var server = serverContext.CreateSecureChannel(loopback.ServerChannel))
                using (var client = clientContext.CreateSecureChannel(loopback.ClientChannel))
                {
                    Echo(server);

                    await client.HandShakeAsync();
                    var outputBuffer = client.Output.Alloc();
                    outputBuffer.WriteUtf8String(_shortTestString);
                    await outputBuffer.FlushAsync();

                    //Now check we get the same thing back
                    string resultString;
                    while (true)
                    {
                        var result = await client.Input.ReadAsync();
                        if (result.Buffer.Length >= _shortTestString.Length)
                        {
                            resultString = result.Buffer.GetUtf8String();
                            client.Input.Advance(result.Buffer.End);
                            break;
                        }
                        client.Input.Advance(result.Buffer.Start, result.Buffer.End);
                    }
                    Assert.Equal(_shortTestString, resultString);
                }
            }
        }

        [WindowsOnlyFact()]
        public async Task SspiChannelServerStreamClient()
        {
            using (var channelFactory = new ChannelFactory())
            using (var cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (var secContext = new SecurityContext(channelFactory, "CARoot", true, cert))
            {
                var loopback = new LoopbackChannel(channelFactory);
                using (var server = secContext.CreateSecureChannel(loopback.ServerChannel))
                using (var sslStream = new SslStream(loopback.ClientChannel.GetStream(), false, ValidateServerCertificate, null, EncryptionPolicy.RequireEncryption))
                {
                    Echo(server);

                    await sslStream.AuthenticateAsClientAsync("CARoot");

                    byte[] messsage = Encoding.UTF8.GetBytes(_shortTestString);
                    sslStream.Write(messsage);
                    sslStream.Flush();
                    // Read message from the server.
                    string serverMessage = ReadMessageFromStream(sslStream);
                    Assert.Equal(_shortTestString, serverMessage);
                }
            }
        }

        [WindowsOnlyFact()]
        public async Task SspiStreamServerChannelClient()
        {
            using (var cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var clientContext = new SecurityContext(factory, "CARoot", false, null))
            {
                var loopback = new LoopbackChannel(factory);
                using (var client = clientContext.CreateSecureChannel(loopback.ClientChannel))
                using (var secureServer = new SslStream(loopback.ServerChannel.GetStream(), false))
                {
                    secureServer.AuthenticateAsServerAsync(cert, false, System.Security.Authentication.SslProtocols.Tls, false);

                    await client.HandShakeAsync();

                    var buff = client.Output.Alloc();
                    buff.WriteUtf8String(_shortTestString);
                    await buff.FlushAsync();

                    //Check that the server actually got it
                    var tempBuff = new byte[_shortTestString.Length];
                    int totalRead = 0;
                    while (true)
                    {
                        int numberOfBytes = secureServer.Read(tempBuff, totalRead, _shortTestString.Length - totalRead);
                        if (numberOfBytes == -1)
                        {
                            break;
                        }
                        totalRead += numberOfBytes;
                        if (totalRead >= _shortTestString.Length)
                        {
                            break;
                        }
                    }
                    Assert.Equal(_shortTestString, UTF8Encoding.UTF8.GetString(tempBuff));
                }
            }
        }

        [Fact]
        public async Task OpenSslChannelServerStreamClient()
        {
            using (var channelFactory = new ChannelFactory())
            using (var cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (var secContext = new OpenSslSecurityContext(channelFactory, "CARoot", true, _certificatePath, _certificatePassword))
            {
                var loopback = new LoopbackChannel(channelFactory);
                using (var server = secContext.CreateSecureChannel(loopback.ServerChannel))
                using (var sslStream = new SslStream(loopback.ClientChannel.GetStream(), false, ValidateServerCertificate, null, EncryptionPolicy.RequireEncryption))
                {
                    Echo(server);
                    await sslStream.AuthenticateAsClientAsync("CARoot");
                    byte[] messsage = Encoding.UTF8.GetBytes(_shortTestString);
                    sslStream.Write(messsage);
                    sslStream.Flush();
                    // Read message from the server.
                    string serverMessage = ReadMessageFromStream(sslStream);
                    Assert.Equal(_shortTestString, serverMessage);
                }
            }
        }

        [Fact]
        public async Task OpenSslStreamServerChannelClient()
        {
            using (var cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory channelFactory = new ChannelFactory())
            using (var clientContext = new OpenSslSecurityContext(channelFactory, "CARoot", false, _certificatePath, _certificatePassword))
            {
                var loopback = new LoopbackChannel(channelFactory);
                using (var secureServer = new SslStream(loopback.ServerChannel.GetStream(), false))
                using (var client = clientContext.CreateSecureChannel(loopback.ClientChannel))
                {
                    secureServer.AuthenticateAsServerAsync(cert, false, System.Security.Authentication.SslProtocols.Tls, false);

                    await client.HandShakeAsync();
                    var buff = client.Output.Alloc();
                    buff.WriteUtf8String(_shortTestString);
                    await buff.FlushAsync();

                    //Check that the server actually got it
                    var tempBuff = new byte[_shortTestString.Length];
                    int totalRead = 0;
                    while (true)
                    {
                        int numberOfBytes = secureServer.Read(tempBuff, totalRead, _shortTestString.Length - totalRead);
                        if (numberOfBytes == -1)
                        {
                            break;
                        }
                        totalRead += numberOfBytes;
                        if (totalRead >= _shortTestString.Length)
                        {
                            break;
                        }
                    }
                    Assert.Equal(_shortTestString, UTF8Encoding.UTF8.GetString(tempBuff));
                }
            }
        }

        private string ReadMessageFromStream(SslStream sslStream)
        {
            byte[] buffer = new byte[2048];
            StringBuilder messageData = new StringBuilder();
            int bytes = -1;
            do
            {
                bytes = sslStream.Read(buffer, 0, buffer.Length);
                Decoder decoder = Encoding.UTF8.GetDecoder();
                char[] chars = new char[decoder.GetCharCount(buffer, 0, bytes)];
                decoder.GetChars(buffer, 0, bytes, chars, 0);
                messageData.Append(chars);
                if (messageData.Length == _shortTestString.Length)
                {
                    break;
                }
            } while (bytes != 0);

            return messageData.ToString();
        }

        public bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None || sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            {
                return true;
            }
            return false;
        }

        private async Task Echo(ISecureChannel channel)
        {
            await channel.HandShakeAsync();
            try
            {
                while (true)
                {
                    var result = await channel.Input.ReadAsync();
                    var request = result.Buffer;

                    if (request.IsEmpty && result.IsCompleted)
                    {
                        channel.Input.Advance(request.End);
                        break;
                    }
                    int len = request.Length;
                    var response = channel.Output.Alloc();
                    response.Append(request);
                    await response.FlushAsync();
                    channel.Input.Advance(request.End);
                }
                channel.Input.Complete();
                channel.Output.Complete();
            }
            catch (Exception ex)
            {
                channel.Input.Complete(ex);
                channel.Output.Complete(ex);
            }
            finally
            {
                channel?.Dispose();
            }
        }
    }
}
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
