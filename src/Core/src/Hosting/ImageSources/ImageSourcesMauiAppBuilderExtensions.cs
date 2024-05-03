using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Hosting.Internal;
using Polly;
using Polly.Timeout;

namespace Microsoft.Maui.Hosting
{
	public static class ImageSourcesMauiAppBuilderExtensions
	{
		public static MauiAppBuilder ConfigureImageSources(this MauiAppBuilder builder)
		{
			builder.ConfigureImageSources(services =>
			{
				services.AddService<IFileImageSource>(svcs => new FileImageSourceService(svcs.CreateLogger<FileImageSourceService>()));
				services.AddService<IFontImageSource>(svcs => new FontImageSourceService(svcs.GetRequiredService<IFontManager>(), svcs.CreateLogger<FontImageSourceService>()));
				services.AddService<IStreamImageSource>(svcs => new StreamImageSourceService(svcs.CreateLogger<StreamImageSourceService>()));
				services.AddService<IUriImageSource>(svcs => new UriImageSourceService(svcs.CreateLogger<UriImageSourceService>()));
			});

			return builder;
		}

		const string HttpUserAgent = "Mozilla/5.0 AppleWebKit Chrome Mobile Safari";

		const string HttpClientKey = "imagesource";

		internal static MauiAppBuilder ConfigureImageSourceHttpClient(this MauiAppBuilder builder,
			Action<HttpClient>? configureDelegate = null, Func<IHttpClientBuilder, IHttpClientBuilder>? delegateBuilder = null)
		{
			IHttpClientBuilder clientBuilder;

			if (configureDelegate != null)
			{
				clientBuilder = builder.Services.AddHttpClient(HttpClientKey, configureDelegate);
			}
			else
			{
				var retryPolicy = Policy
					.HandleResult<HttpResponseMessage>(r =>
						r.StatusCode == HttpStatusCode.GatewayTimeout
						|| r.StatusCode == HttpStatusCode.RequestTimeout)
					.Or<HttpRequestException>()
					.Or<TimeoutRejectedException>()
					.WaitAndRetryAsync(new[]
					{
						TimeSpan.FromSeconds(2),
						TimeSpan.FromSeconds(3),
					});

				clientBuilder = builder.Services.AddHttpClient(HttpClientKey, client =>
					{
						client.DefaultRequestHeaders.Add("User-Agent", HttpUserAgent);
					})
					.ConfigurePrimaryHttpMessageHandler(() =>
					{
						var handler = new HttpClientHandler();
						if (handler.SupportsAutomaticDecompression)
						{
							handler.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
						}

						return handler;
					})
					.AddPolicyHandler(retryPolicy);
			}

			delegateBuilder?.Invoke(clientBuilder);

			//do not slow us down with logs spam
			//one could inject IHttpMessageHandlerBuilderFilter after this to enable logs back
			builder.Services.RemoveAll<IHttpMessageHandlerBuilderFilter>();

			return builder;
		}

		public static MauiAppBuilder ConfigureImageSources(this MauiAppBuilder builder, Action<IImageSourceServiceCollection>? configureDelegate)
		{
			if (configureDelegate != null)
			{
				builder.Services.AddSingleton<ImageSourceRegistration>(new ImageSourceRegistration(configureDelegate));
			}

			builder.Services.TryAddSingleton<IImageSourceServiceProvider>(svcs => new ImageSourceServiceProvider(svcs.GetRequiredService<IImageSourceServiceCollection>(), svcs));
			builder.Services.TryAddSingleton<IImageSourceServiceCollection>(svcs => new ImageSourceServiceBuilder(svcs.GetServices<ImageSourceRegistration>()));

			return builder;
		}

		class ImageSourceRegistration
		{
			private readonly Action<IImageSourceServiceCollection> _registerAction;

			public ImageSourceRegistration(Action<IImageSourceServiceCollection> registerAction)
			{
				_registerAction = registerAction;
			}

			internal void AddRegistration(IImageSourceServiceCollection builder)
			{
				_registerAction(builder);
			}
		}

		class ImageSourceServiceBuilder : MauiServiceCollection, IImageSourceServiceCollection
		{
			public ImageSourceServiceBuilder(IEnumerable<ImageSourceRegistration> registrationActions)
			{
				if (registrationActions != null)
				{
					foreach (var effectRegistration in registrationActions)
					{
						effectRegistration.AddRegistration(this);
					}
				}
			}
		}

		internal static HttpClient? CreateImageSourceHttpClient(this IServiceProvider services)
		{
			return services.GetService<IHttpClientFactory>()?.CreateClient(HttpClientKey);
		}

	}
}
