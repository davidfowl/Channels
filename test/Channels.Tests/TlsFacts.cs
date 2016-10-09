using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Channels.Networking.Sockets;
using Channels.Networking.TLS;
using Channels.Text.Primitives;
using Microsoft.Extensions.PlatformAbstractions;
using Xunit;

namespace Channels.Tests
{
    public class TlsFacts
    {
        [Fact]
        public async Task EncryptDecryptStress()
        {
            var testString = "The quick brown fox jumped over the lazy dog";
            var cetPath = System.IO.Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "data", "TestCert.pfx");
            X509Certificate cert = new X509Certificate(cetPath, "Test123t");

            using (ChannelFactory factory = new ChannelFactory())
            {

                var serverContext = new SecurityContext(factory, "Test", true, cert);
                var clientContext = new SecurityContext(factory, "Test", false, null);

                var lowerClientChannel = new TestingIChannel(factory);
                var lowerServerChannel = new TestingIChannel(lowerClientChannel.RawOutput, lowerClientChannel.RawInput);

                var serverChannel = serverContext.CreateSecureChannel(lowerServerChannel);
                var clientChannel = clientContext.CreateSecureChannel(lowerClientChannel);

                var writeBuffer = serverChannel.Output.Alloc();
                writeBuffer.WriteUtf8String(testString);
                await writeBuffer.FlushAsync();

                //Get the decrypted string
                var results = await clientChannel.Input.ReadAsync();

                //Finally we should have the same string on the unencrypted output
                var resultString = results.Buffer.GetUtf8String();

                clientChannel.Input.Advance(results.Buffer.End, results.Buffer.End);

                Assert.Equal(testString, resultString);

                lowerClientChannel.Input.Complete();
                lowerClientChannel.Output.Complete();

                serverChannel.Dispose();
                clientChannel.Dispose();
            }
        }

        [Fact]
        public async Task ServerChannelStreamClient()
        {
            using (var channelFactory = new ChannelFactory())
            {
                var cetPath = System.IO.Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "data", "TestCert.pfx");
                var cert = new X509Certificate(cetPath, "Test123t");
                using (var secContext = new SecurityContext(channelFactory, "localhost", true, cert))
                {
                    var ip = new IPEndPoint(IPAddress.Loopback, 5010);
                    using (var server = new SocketListener())
                    {
                        server.OnConnection((c) =>  Echo(secContext.CreateSecureChannel(c)));
                        server.Start(ip);
                        using (var client = new TcpClient())
                        {
                            await client.ConnectAsync(ip.Address, ip.Port);
                            SslStream sslStream = new SslStream(client.GetStream(), false, ValidateServerCertificate, null,EncryptionPolicy.RequireEncryption);
                            await sslStream.AuthenticateAsClientAsync("localhost");
                            byte[] messsage = Encoding.UTF8.GetBytes("there's a function for that<EOF>");
                            sslStream.Write(messsage);
                            sslStream.Flush();
                            // Read message from the server.
                            string serverMessage = ReadMessage(sslStream);
                            Assert.Equal("there's a function for that<EOF>",serverMessage);
                        }
                    }
                }
            }
        }
        string ReadMessage(SslStream sslStream)
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
                if (messageData.ToString().IndexOf("<EOF>") != -1)
                {
                    break;
                }
            } while (bytes != 0);

            return messageData.ToString();
        }

        public bool ValidateServerCertificate(
              object sender,
              X509Certificate certificate,
              X509Chain chain,
              SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        private async void Echo(IChannel channel)
        {
            
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
                    response.Append(ref request);
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