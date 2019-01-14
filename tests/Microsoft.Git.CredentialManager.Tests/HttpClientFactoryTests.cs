// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System.Net.Http;
using Xunit;

namespace Microsoft.Git.CredentialManager.Tests
{
    public class HttpClientFactoryTests
    {
        [Fact]
        public void HttpClientFactory_GetClient_SetsDefaultHeaders()
        {
            var factory = new HttpClientFactory();

            HttpClient client = factory.CreateClient();

            Assert.NotNull(client);
            Assert.Equal(Constants.GetHttpUserAgent(), client.DefaultRequestHeaders.UserAgent.ToString());
            Assert.True(client.DefaultRequestHeaders.CacheControl.NoCache);
        }

        [Fact]
        public void HttpClientFactory_GetClient_MultipleCalls_ReturnsNewInstance()
        {
            var factory = new HttpClientFactory();

            HttpClient client1 = factory.CreateClient();
            HttpClient client2 = factory.CreateClient();

            Assert.NotSame(client1, client2);
        }
    }
}
