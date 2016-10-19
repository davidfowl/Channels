﻿using System;
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

        [WindowsOnlyFact]
        public async Task AplnMatchingProtocol()
        {
            using (X509Certificate cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var serverContext = new SecurityContext(factory, "CARoot", true, cert, ApplicationProtocols.ProtocolIds.Http11 | ApplicationProtocols.ProtocolIds.Http2overTLS))
            using (var clientContext = new SecurityContext(factory, "CARoot", false, null, ApplicationProtocols.ProtocolIds.Http2overTLS))
            {
                var loopback = new LoopbackChannel(factory);
                Echo(serverContext.CreateSecureChannel(loopback.ServerChannel));
                using (var client = clientContext.CreateSecureChannel(loopback.ClientChannel))
                {
                    var proto = await client.HandShakeAsync();
                    Assert.Equal(ApplicationProtocols.ProtocolIds.Http2overTLS, proto);
                }
            }
        }

        [WindowsOnlyFact]
        public async Task OpenSslAsServerAplnMatchingProtocol()
        {
            using (X509Certificate cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var serverContext = new OpenSslSecurityContext(factory, "test", true, _certificatePath, _certificatePassword, ApplicationProtocols.ProtocolIds.Http11 | ApplicationProtocols.ProtocolIds.Http2overTLS))
            using (var clientContext = new SecurityContext(factory, "CARoot", false, cert, ApplicationProtocols.ProtocolIds.Http2overTLS))
            {
                var loopback = new LoopbackChannel(factory);
                Echo(serverContext.CreateSecureChannel(loopback.ServerChannel));
                using (var client = clientContext.CreateSecureChannel(loopback.ClientChannel))
                {
                    var proto = await client.HandShakeAsync();
                    Assert.Equal(ApplicationProtocols.ProtocolIds.Http2overTLS, proto);
                }
            }
        }

        [WindowsOnlyFact]
        public async Task OpenSslAsClientAplnMatchingProtocol()
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

        [Fact]
        public async Task OpenSslAndSSPIChannelAllTheThings()
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

        [WindowsOnlyFact]
        public async Task SSPIEncryptDecryptChannelsAllThings()
        {
            using (X509Certificate cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var serverContext = new SecurityContext(factory, "CARoot", true, cert))
            using (var clientContext = new SecurityContext(factory, "CARoot", false, null))
            {
                var loopback = new LoopbackChannel(factory);
                Echo(serverContext.CreateSecureChannel(loopback.ServerChannel));

                using (var client = clientContext.CreateSecureChannel(loopback.ClientChannel))
                {
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

        [WindowsOnlyFact(Skip = "Anything with tcp is broken for now")]
        public async Task ServerChannelStreamClient()
        {
            var ip = new IPEndPoint(IPAddress.Loopback, 5023);

            using (var channelFactory = new ChannelFactory())
            using (var cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (var secContext = new SecurityContext(channelFactory, "CARoot", true, cert))
            {
                var server = new UvTcpListener(new UvThread(), ip);
                server.OnConnection((c) => Echo(secContext.CreateSecureChannel(c)));
                server.Start();
                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(ip.Address, ip.Port);
                    SslStream sslStream = new SslStream(client.GetStream(), false, ValidateServerCertificate, null, EncryptionPolicy.RequireEncryption);
                    await sslStream.AuthenticateAsClientAsync("CARoot");
                    byte[] messsage = Encoding.UTF8.GetBytes(_shortTestString);
                    sslStream.Write(messsage);
                    sslStream.Flush();
                    // Read message from the server.
                    string serverMessage = ReadMessageFromStream(sslStream);
                    Assert.Equal(_shortTestString, serverMessage);
                }
                server.Stop();
            }
        }

        private string ReadMessageFromStream(SslStream sslStream)
        {
            // Read the  message sent by the server.
            // The end of the message is signaled using the
            // "<EOF>" marker.
            byte[] buffer = new byte[2048];
            StringBuilder messageData = new StringBuilder();
            int bytes = -1;
            do
            {
                bytes = sslStream.Read(buffer, 0, buffer.Length);

                // Use Decoder class to convert from bytes to UTF8
                // in case a character spans two buffers.
                Decoder decoder = Encoding.UTF8.GetDecoder();
                char[] chars = new char[decoder.GetCharCount(buffer, 0, bytes)];
                decoder.GetChars(buffer, 0, bytes, chars, 0);
                messageData.Append(chars);
                // Check for EOF.
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

        [WindowsOnlyFact(Skip = "Issues with the sockets client")]
        public async Task StreamServerChannelClient()
        {
            var ip = new IPEndPoint(IPAddress.Loopback, 5024);
            using (var cert = new X509Certificate(_certificatePath, _certificatePassword))
            using (ChannelFactory factory = new ChannelFactory())
            using (var clientContext = new SecurityContext(factory, "CARoot", false, null))
            {
                var listener = new TcpListener(ip);
                listener.Start();

                var client = clientContext.CreateSecureChannel(await SocketConnection.ConnectAsync(ip, factory));
                var server = await listener.AcceptTcpClientAsync();
                using (var secureServer = new SslStream(server.GetStream(), false))
                {
                    await secureServer.AuthenticateAsServerAsync(cert, false, System.Security.Authentication.SslProtocols.Tls, false);
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
    }
}
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
