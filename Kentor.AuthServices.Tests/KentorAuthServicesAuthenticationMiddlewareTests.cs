﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Kentor.AuthServices.Owin;
using FluentAssertions;
using Microsoft.Owin.Security.Infrastructure;
using Microsoft.Owin;
using Owin;
using Microsoft.Owin.Security;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Kentor.AuthServices.TestHelpers;
using System.IO;
using System.Text;
using System.Security.Claims;
using Kentor.AuthServices.Configuration;
using System.Net.Http;
using NSubstitute;

namespace Kentor.AuthServices.Tests
{
    [TestClass]
    public class KentorAuthServicesAuthenticationMiddlewareTests
    {
        class ProtectedCaller : KentorAuthServicesAuthenticationMiddleware
        {
            public ProtectedCaller(OwinMiddleware next, IAppBuilder app,
                KentorAuthServicesAuthenticationOptions options)
                : base(next, app, options)
            { }

            public AuthenticationHandler<KentorAuthServicesAuthenticationOptions> CallCreateHandler()
            {
                return CreateHandler();
            }
        }

        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_CtorNullChecksOptions()
        {
            Action a = () => new KentorAuthServicesAuthenticationMiddleware(
                new StubOwinMiddleware(0, null), CreateAppBuilder(),
                null);

            a.ShouldThrow<ArgumentNullException>("options");
        }

        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_CtorNullChecksApp()
        {
            Action a = () => new KentorAuthServicesAuthenticationMiddleware(
                new StubOwinMiddleware(0, null), null, new KentorAuthServicesAuthenticationOptions());

            a.ShouldThrow<ArgumentNullException>("app");
        }

        const string DefaultSignInAsAuthenticationType = "MyDefaultSignAsAuthTypeForTesting";

        private static IAppBuilder CreateAppBuilder()
        {
            var app = Substitute.For<IAppBuilder>();
            app.Properties.Returns(new Dictionary<string, object>());
            app.SetDefaultSignInAsAuthenticationType(DefaultSignInAsAuthenticationType);
            return app;
        }

        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_CtorSetsDefaultAuthOption()
        {
            var options = new KentorAuthServicesAuthenticationOptions();

            options.SignInAsAuthenticationType.Should().BeNull();

            var middleware = new KentorAuthServicesAuthenticationMiddleware(new StubOwinMiddleware(0, null),  
                CreateAppBuilder(), options);

            options.SignInAsAuthenticationType.Should().Be(DefaultSignInAsAuthenticationType);
        }

        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_RedirectsOnAuthChallenge()
        {
            var middleware = new KentorAuthServicesAuthenticationMiddleware(
                new StubOwinMiddleware(401, new AuthenticationResponseChallenge(
                    new string[] { "KentorAuthServices" }, null)), CreateAppBuilder(),
                new KentorAuthServicesAuthenticationOptions());

            var context = OwinTestHelpers.CreateOwinContext();

            middleware.Invoke(context).Wait();

            context.Response.StatusCode.Should().Be(302);
            context.Response.Headers["Location"].Should().StartWith("https://idp.example.com/idp");
        }

        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_NoRedirectOnNon401()
        {
            var middleware = new KentorAuthServicesAuthenticationMiddleware(
                new StubOwinMiddleware(200, new AuthenticationResponseChallenge(
                    new string[] { "KentorAuthServices" }, null)), CreateAppBuilder(),
                new KentorAuthServicesAuthenticationOptions());

            var context = OwinTestHelpers.CreateOwinContext();

            middleware.Invoke(context).Wait();

            context.Response.StatusCode.Should().Be(200);
            context.Response.Headers["Location"].Should().BeNull();
        }

        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_NoRedirectWithoutChallenge()
        {
            var middleware = new KentorAuthServicesAuthenticationMiddleware(
                new StubOwinMiddleware(401, null), CreateAppBuilder(),
                new KentorAuthServicesAuthenticationOptions());

            var context = OwinTestHelpers.CreateOwinContext();

            middleware.Invoke(context).Wait();

            context.Response.StatusCode.Should().Be(401);
        }

        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_RedirectoToSecondIdp_AuthenticationProperties()
        {
            var secondIdp = IdentityProvider.ConfiguredIdentityProviders.Skip(1).First().Value;
            var secondDestination = secondIdp.DestinationUri;
            var secondEntityId = secondIdp.Issuer;

            var middleware = new KentorAuthServicesAuthenticationMiddleware(
                new StubOwinMiddleware(401, new AuthenticationResponseChallenge(
                    new string[] { "KentorAuthServices" }, new AuthenticationProperties(
                        new Dictionary<string, string>()
                        {
                            { "idp", secondEntityId }
                        }))), 
                        CreateAppBuilder(), new KentorAuthServicesAuthenticationOptions());

            var context = OwinTestHelpers.CreateOwinContext();
            middleware.Invoke(context).Wait();

            context.Response.StatusCode.Should().Be(302);
            context.Response.Headers["Location"].Should().StartWith(secondDestination.ToString());
        }

        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_RedirectoToSecondIdp_OwinEnvironment()
        {
            var secondIdp = IdentityProvider.ConfiguredIdentityProviders.Skip(1).First().Value;
            var secondDestination = secondIdp.DestinationUri;
            var secondEntityId = secondIdp.Issuer;

            var middleware = new KentorAuthServicesAuthenticationMiddleware(
                new StubOwinMiddleware(401, new AuthenticationResponseChallenge(
                    new string[] { "KentorAuthServices" }, new AuthenticationProperties())),
                        CreateAppBuilder(), new KentorAuthServicesAuthenticationOptions());

            var context = OwinTestHelpers.CreateOwinContext();
            context.Environment["KentorAuthServices.idp"] = secondEntityId;
            middleware.Invoke(context).Wait();

            context.Response.StatusCode.Should().Be(302);
            context.Response.Headers["Location"].Should().StartWith(secondDestination.ToString());
        }

        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_RedirectOnChallengeForAuthTypeInOptions()
        {
            var authenticationType = "someAuthName";

            var middleware = new KentorAuthServicesAuthenticationMiddleware(
                new StubOwinMiddleware(401, new AuthenticationResponseChallenge(
                    new string[] { authenticationType }, null)),
                CreateAppBuilder(),
                new KentorAuthServicesAuthenticationOptions()
                {
                    AuthenticationType = authenticationType
                });

            var context = OwinTestHelpers.CreateOwinContext();

            middleware.Invoke(context).Wait();

            context.Response.StatusCode.Should().Be(302);
            context.Response.Headers["Location"].Should().StartWith("https://idp.example.com/idp");
        }

        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_RedirectRemembersReturnPath()
        {
            var returnUri = "http://sp.example.com/returnuri";

            var middleware = new KentorAuthServicesAuthenticationMiddleware(
                new StubOwinMiddleware(401, new AuthenticationResponseChallenge(
                    new string[] { "KentorAuthServices" }, new AuthenticationProperties()
                    {
                        RedirectUri = returnUri
                    })),
                    CreateAppBuilder(), new KentorAuthServicesAuthenticationOptions());

            var context = OwinTestHelpers.CreateOwinContext();

            middleware.Invoke(context).Wait();

            var requestId = AuthnRequestHelper.GetRequestId(new Uri(context.Response.Headers["Location"]));

            StoredRequestState storedAuthnData;
            PendingAuthnRequests.TryRemove(new System.IdentityModel.Tokens.Saml2Id(requestId), out storedAuthnData);

            storedAuthnData.ReturnUri.Should().Be(returnUri);
        }

        [NotReRunnable]
        [TestMethod]
        public void KentorAuthServicesAuthenticationMiddleware_AcsWorks()
        {
            var context = OwinTestHelpers.CreateOwinContext();
            context.Request.Method = "POST";

            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID = ""KentorAuthServicesAuthenticationMiddleware_AcsWorks"" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>
                    https://idp.example.com
                </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion
                Version=""2.0"" ID=""KentorAuthServicesAuthenticationMiddleware_AcsWorks_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
            </saml2p:Response>";

            var bodyData = new KeyValuePair<string, string>[] { 
                new KeyValuePair<string, string>("SAMLResponse", 
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(SignedXmlHelper.SignXml(response))))
            };

            var encodedBodyData = new FormUrlEncodedContent(bodyData);

            context.Request.Body = encodedBodyData.ReadAsStreamAsync().Result;
            context.Request.ContentType = encodedBodyData.Headers.ContentType.ToString();
            context.Request.Host = new HostString("localhost");
            context.Request.Path = new PathString("/Saml2AuthenticationModule/acs");

            var signInAsAuthenticationType = "AuthType";
            var ids = new ClaimsIdentity[] { new ClaimsIdentity(signInAsAuthenticationType),
                new ClaimsIdentity(signInAsAuthenticationType) };
            ids[0].AddClaim(new Claim(ClaimTypes.NameIdentifier, "SomeUser", null, "https://idp.example.com"));
            ids[1].AddClaim(new Claim(ClaimTypes.Role, "RoleFromClaimsAuthManager", 
                null, "ClaimsAuthenticationManagerMock"));

            var middleware = new KentorAuthServicesAuthenticationMiddleware(null, CreateAppBuilder(),
                new KentorAuthServicesAuthenticationOptions()
                {
                    SignInAsAuthenticationType = "AuthType"
                });

            middleware.Invoke(context).Wait();

            context.Response.StatusCode.Should().Be(302);
            context.Response.Headers["Location"].Should().Be("http://localhost/LoggedIn");

            context.Authentication.AuthenticationResponseGrant.Principal.Identities.ShouldBeEquivalentTo(ids,
                opt => opt.IgnoringCyclicReferences());
        }
    }
}
