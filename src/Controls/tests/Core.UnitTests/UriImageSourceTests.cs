using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using NSubstitute;
using Polly;
using Xunit;
using IOPath = System.IO.Path;

namespace Microsoft.Maui.Controls.Core.UnitTests
{

	public class UriImageSourceTests : BaseTestFixture
	{

		public UriImageSourceTests()
		{

			networkcalls = 0;
		}

		static Random rnd = new Random();
		static int networkcalls = 0;
		static async Task<Stream> GetStreamAsync(Uri uri, CancellationToken cancellationToken)
		{
			await Task.Delay(rnd.Next(30, 2000));
			if (cancellationToken.IsCancellationRequested)
				throw new TaskCanceledException();
			networkcalls++;
			return typeof(UriImageSourceTests).Assembly.GetManifestResourceStream(uri.LocalPath.Substring(1));
		}

		IMauiContext SetupContext()
		{
			var mauiApp = MauiApp.CreateBuilder(useDefaults: false)
				.ConfigureImageSourceHttpClient()
				.Build();

			var services = mauiApp.Services;
			var context = Substitute.For<IMauiContext>();
			context.Services.Returns(services);

			return context;
		}

		void SetupApplicationWithHttpClient(MauiApp mauiApp = null)
		{
			if (mauiApp == null)
			{
				mauiApp = MauiApp.CreateBuilder(useDefaults: false)
					.ConfigureImageSourceHttpClient()
					.Build();
			}

			var fakeMauiContext = Substitute.For<IMauiContext>();
			var fakeHandler = Substitute.For<IElementHandler>();
			fakeMauiContext.Services.Returns(mauiApp.Services);
			fakeHandler.MauiContext.Returns(fakeMauiContext);
			var app = new Application(true);
			app.Handler = fakeHandler;
		}

		[Fact(Skip = "LoadImageFromStream")]
		public void LoadImageFromStream()
		{
			IStreamImageSource loader = new UriImageSource
			{
				Uri = new Uri("http://foo.com/Images/crimson.jpg"),
			};
			Stream stream = loader.GetStreamAsync().Result;

			Assert.Equal(79109, stream.Length);
		}

		[Fact]
		public void ShouldCreateHttpClient()
		{
			var mauiApp = MauiApp.CreateBuilder(useDefaults: false)
				.ConfigureImageSourceHttpClient()
				.Build();

			var client = mauiApp.Services.CreateImageSourceHttpClient();

			Assert.NotNull(client);
		}

		[Fact]
		public void LoadImageFromInternetWIthNonReusableClient()
		{
			IStreamImageSource loader = new UriImageSource
			{
				Uri = new Uri("https://upload.wikimedia.org/wikipedia/commons/1/12/Wikipedia.png"),
			};
			Stream stream = loader.GetStreamAsync().Result;

			Assert.Equal(11742, stream.Length);
		}

		[Fact]
		public void LoadImageFromInternetWIthFactoryClient()
		{
			SetupApplicationWithHttpClient();

			IStreamImageSource loader = new UriImageSource
			{
				Uri = new Uri("https://upload.wikimedia.org/wikipedia/commons/1/12/Wikipedia.png"),
			};
			Stream stream = loader.GetStreamAsync().Result;

			Assert.Equal(11742, stream.Length);
		}

		[Fact]
		public void LoadImageFromInternetWIthCustomizedClient()
		{
			SetupApplicationWithHttpClient(MauiApp.CreateBuilder(useDefaults: false)
				.ConfigureImageSourceHttpClient(
					client =>
					{
						client.DefaultRequestHeaders.Add("User-Agent", "Tests");
					},
					build =>
					{
						build.ConfigurePrimaryHttpMessageHandler(() =>
						{
							var handler = new HttpClientHandler();
							if (handler.SupportsAutomaticDecompression)
							{
								handler.MaxAutomaticRedirections = 2;
							}
							return handler;
						});
					})
				.Build());

			IStreamImageSource loader = new UriImageSource
			{
				Uri = new Uri("https://www.mediawiki.org/w/index.php?title=Special:Redirect/file/Wikipedia.png"),
			};
			Stream stream = loader.GetStreamAsync().Result;

			Assert.Equal(11742, stream.Length);
		}

		[Fact]
		public void LoadImageFromInternetRequiresUserAgent()
		{
			SetupApplicationWithHttpClient();

			IStreamImageSource loader = new UriImageSource
			{
				Uri = new Uri("https://www.mediawiki.org/w/index.php?title=Special:Redirect/file/Wikipedia.png"),
			};
			Stream stream = loader.GetStreamAsync().Result;

			Assert.Equal(11742, stream.Length);
		}

		[Fact(Skip = "SecondCallLoadFromCache")]
		public void SecondCallLoadFromCache()
		{
			IStreamImageSource loader = new UriImageSource
			{
				Uri = new Uri("http://foo.com/Images/crimson.jpg"),
			};
			Assert.Equal(0, networkcalls);

			using (var s0 = loader.GetStreamAsync().Result)
			{
				Assert.Equal(79109, s0.Length);
				Assert.Equal(1, networkcalls);
			}

			using (var s1 = loader.GetStreamAsync().Result)
			{
				Assert.Equal(79109, s1.Length);
				Assert.Equal(1, networkcalls);
			}
		}

		[Fact(Skip = "DoNotKeepFailedRetrieveInCache")]
		public void DoNotKeepFailedRetrieveInCache()
		{
			IStreamImageSource loader = new UriImageSource
			{
				Uri = new Uri("http://foo.com/missing.png"),
			};
			Assert.Equal(0, networkcalls);

			var s0 = loader.GetStreamAsync().Result;
			Assert.Null(s0);
			Assert.Equal(1, networkcalls);

			var s1 = loader.GetStreamAsync().Result;
			Assert.Null(s1);
			Assert.Equal(2, networkcalls);
		}

		[Fact(Skip = "ConcurrentCallsOnSameUriAreQueued")]
		public void ConcurrentCallsOnSameUriAreQueued()
		{
			IStreamImageSource loader = new UriImageSource
			{
				Uri = new Uri("http://foo.com/Images/crimson.jpg"),
			};
			Assert.Equal(0, networkcalls);

			var t0 = loader.GetStreamAsync();
			var t1 = loader.GetStreamAsync();

			//var s0 = t0.Result;
			using (var s1 = t1.Result)
			{
				Assert.Equal(1, networkcalls);
				Assert.Equal(79109, s1.Length);
			}
		}

		[Fact]
		public void NullUriDoesNotCrash()
		{
			var loader = new UriImageSource();
			loader.Uri = null;
		}

		[Fact]
		public void UrlHashKeyAreTheSame()
		{
			var urlHash1 = Crc64.ComputeHashString("http://www.optipess.com/wp-content/uploads/2010/08/02_Bad-Comics6-10.png?a=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbasdasdasdasdasasdasdasdasdasd");
			var urlHash2 = Crc64.ComputeHashString("http://www.optipess.com/wp-content/uploads/2010/08/02_Bad-Comics6-10.png?a=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbasdasdasdasdasasdasdasdasdasd");
			Assert.True(urlHash1 == urlHash2);
		}

		[Fact]
		public void UrlHashKeyAreNotTheSame()
		{
			var urlHash1 = Crc64.ComputeHashString("http://www.optipess.com/wp-content/uploads/2010/08/02_Bad-Comics6-10.png?a=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbasdasdasdasdasasdasdasdasdasd");
			var urlHash2 = Crc64.ComputeHashString("http://www.optipess.com/wp-content/uploads/2010/08/02_Bad-Comics6-10.png?a=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbasdasdasdasdasasdasda");
			Assert.True(urlHash1 != urlHash2);
		}

	}
}
