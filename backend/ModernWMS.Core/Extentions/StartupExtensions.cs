using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Filters;
using System.Reflection;
using ModernWMS.Core.DBContext;
using Microsoft.Extensions.Logging.Debug;
using ModernWMS.Core.Swagger;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Middleware;
using Newtonsoft.Json;
using ModernWMS.Core.DI;
using Microsoft.Extensions.Localization;
using Hangfire;
using Hangfire.MemoryStorage;

namespace ModernWMS.Core.Extentions
{
    public static class StartupExtensions
    {
        public static void AddExtensionsService(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
        {
            services.AddLocalization();
            services.AddSingleton<IStringLocalizer>((sp) =>
            {
                var sharedLocalizer = sp.GetRequiredService<IStringLocalizer<MultiLanguage>>();
                return sharedLocalizer;
            });
            services.AddHttpClient();
            services.AddHealthChecks();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<CacheManager>();
            services.AddSingleton<IMemoryCache>(factory =>
            {
                var cache = new MemoryCache(new MemoryCacheOptions());
                return cache;
            });

            var connectionString = configuration.GetConnectionString("MySqlConn");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:MySqlConn is required.");
            }

            services.AddDbContextPool<SqlDBContext>(t =>
            {
                t.UseMySQL(connectionString, b => b.MigrationsAssembly("ModernWMS"));
                if (environment.IsDevelopment())
                {
                    t.EnableSensitiveDataLogging();
                    t.UseLoggerFactory(new LoggerFactory(new[] { new DebugLoggerProvider() }));
                }
            }, 100);
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            services.AddCors(options => options.AddPolicy("Frontend", policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                }
            }));
            services.AddMemoryCache();
            services.AddScoped<MultiTenancy.ITenantProvider, MultiTenancy.TenantProvider>();
            services.AddSwaggerService(configuration, AppContext.BaseDirectory);
            services.AddTokenGeneratorService(configuration);
            services.RegisterAssembly();
            services.AddControllers(c =>
            {
                c.Filters.Add(typeof(ViewModelActionFiter));
                c.MaxModelValidationErrors = 99999;
            }).ConfigureApiBehaviorOptions(o =>
            {
                o.SuppressModelStateInvalidFilter = true;
            })//format
              .AddNewtonsoftJson(options =>
              {
                  options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                  options.SerializerSettings.DateFormatString = "yyyy-MM-dd HH:mm:ss";
                  options.SerializerSettings.Converters.Add(new JsonStringTrimConverter());
                  options.SerializerSettings.Formatting = Formatting.Indented;
                  options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
              }).AddDataAnnotationsLocalization(options =>
              {
                  options.DataAnnotationLocalizerProvider = (type, factory) =>
                      factory.Create(typeof(ModernWMS.Core.MultiLanguage));
              });

            // Hangfire
            services.AddHangfire(x => x.SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseStorage(new MemoryStorage()));
            services.AddHangfireServer(options =>
            {
                options.ServerName = $"{Environment.MachineName}.{Guid.NewGuid()}";
                options.WorkerCount = Environment.ProcessorCount * 5;
                options.Queues = ["wms"];
            });
            services.AddScoped<FunctionHelper>();
        }

        public static void UseExtensionsConfigure(this IApplicationBuilder app, IWebHostEnvironment env, IServiceProvider serviceProvider, IConfiguration configuration)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseStaticFiles();
            app.UseSwaggerConfigure(configuration);
            app.UseRouting();
            app.UseCors("Frontend");
            app.UseTokenGeneratorConfigure(configuration);
            app.UseAuthorization();
            app.UseMiddleware<GlobalExceptionMiddleware>();
            var support_languages = new[] { "zh-cn", "en-us" };
            var localization_options = new RequestLocalizationOptions()
                .SetDefaultCulture(support_languages[0])
                .AddSupportedCultures(support_languages)
                .AddSupportedUICultures(support_languages);
            app.UseRequestLocalization(localization_options);

            app.UseHangfireDashboard();
            AddHangfireJob(serviceProvider);
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health");
                endpoints.MapControllers();
            });
        }

        #region Swagger

        /// <summary>
        /// Swagger
        /// </summary>
        /// <param name="services">服务容器</param>
        /// <param name="configuration">配置文件</param>
        /// <param name="BaseDirectory">主目录</param>
        private static void AddSwaggerService(this IServiceCollection services, IConfiguration configuration, string BaseDirectory)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            var swaggerSettings = configuration.GetSection("SwaggerSettings");

            var provider = services.Configure<SwaggerSettings>(swaggerSettings).BuildServiceProvider();
            var settings = provider.GetService<IOptions<SwaggerSettings>>()?.Value;

            if (settings != null && settings.Name.Equals("ModernWMS"))
            {
                services.AddSwaggerGen(c =>
                {
                    typeof(CustomApiVersion.ApiVersions).GetEnumNames().ToList().ForEach(version =>
                    {
                        c.SwaggerDoc(version, new OpenApiInfo
                        {
                            Title = settings.ApiTitle,
                            Version = settings.ApiVersion,
                            Description = settings.Description
                        });
                    });

                    if (settings.XmlFiles != null && settings.XmlFiles.Count > 0)
                    {
                        settings.XmlFiles.ForEach(fileName =>
                        {
                            if (File.Exists(Path.Combine(BaseDirectory, fileName)))
                            {
                                c.IncludeXmlComments(Path.Combine(BaseDirectory, fileName), true);
                            }
                        });
                    }

                    c.OperationFilter<AddResponseHeadersFilter>();
                    c.OperationFilter<AppendAuthorizeToSummaryOperationFilter>();
                    c.OperationFilter<SecurityRequirementsOperationFilter>();

                    c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
                    {
                        Description = "please input Bearer {token}",
                        Name = "Authorization",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.ApiKey
                    });
                    c.SwaggerGeneratorOptions.DescribeAllParametersInCamelCase = false;
                });
            }
        }

        /// <summary>
        /// register Swagger
        /// </summary>
        /// <param name="app"></param>
        /// <param name="configuration">配置文件</param>
        private static void UseSwaggerConfigure(this IApplicationBuilder app, IConfiguration configuration)
        {
            var swaggerSettings = configuration.GetSection("SwaggerSettings");

            if (swaggerSettings != null && swaggerSettings["Name"].Equals("ModernWMS"))
            {
                app.UseSwagger();

                app.UseSwaggerUI(c =>
                {
                    typeof(CustomApiVersion.ApiVersions).GetEnumNames().OrderBy(e => e).ToList().ForEach(version =>
                    {
                        c.SwaggerEndpoint($"/swagger/{version}/swagger.json", $"{swaggerSettings["Name"]} {version}");
                    });

                    c.IndexStream = () => Assembly.GetExecutingAssembly().GetManifestResourceStream("ModernWMS.Core.Swagger.index.html");
                    c.RoutePrefix = "";
                });
            }
        }

        #endregion Swagger

        #region JWT

        /// <summary>
        /// register JWT
        /// </summary>
        /// <param name="services">services</param>
        /// <param name="configuration">configuration</param>
        private static void AddTokenGeneratorService(this IServiceCollection services, IConfiguration configuration)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            var tokenSettings = configuration.GetSection("TokenSettings");
            var validatedTokenSettings = tokenSettings.Get<TokenSettings>()
                ?? throw new InvalidOperationException("TokenSettings configuration is required.");
            var validationParameters = TokenValidationParametersFactory.Create(validatedTokenSettings);
            services.Configure<TokenSettings>(tokenSettings);
            services.AddTransient<ITokenManager, TokenManager>();

            services.AddAuthentication(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = nameof(ApiResponseHandler);
                options.DefaultForbidScheme = nameof(ApiResponseHandler);
            }
            )
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = validationParameters;
            })
            .AddScheme<AuthenticationSchemeOptions, ApiResponseHandler>(nameof(ApiResponseHandler), o => { });
        }

        private static void UseTokenGeneratorConfigure(this IApplicationBuilder app, IConfiguration configuration)
        {
            app.UseAuthentication();
        }

        #endregion JWT

        #region dynamic injection

        /// <summary>
        /// judge the dll to be injected by IDependency
        /// </summary>
        /// <param name="services">services</param>
        private static IServiceCollection RegisterAssembly(this IServiceCollection services)
        {
            var path = AppDomain.CurrentDomain.RelativeSearchPath ?? AppDomain.CurrentDomain.BaseDirectory;
            var referencedAssemblies = System.IO.Directory.GetFiles(path, "ModernWMS*.dll").Select(Assembly.LoadFrom).ToArray();

            var types = referencedAssemblies
                .SelectMany(a => a.DefinedTypes)
            .Select(type => type.AsType())
                .Where(x => x != typeof(IDependency) && typeof(IDependency).IsAssignableFrom(x)).ToArray();
            var implementTypes = types.Where(x => x.IsClass).ToArray();
            var interfaceTypes = types.Where(x => x.IsInterface).ToArray();
            foreach (var implementType in implementTypes)
            {
                var interfaceType = interfaceTypes.FirstOrDefault(x => x.IsAssignableFrom(implementType));
                if (interfaceType != null)
                    services.AddScoped(interfaceType, implementType);
            }

            services.AddScoped<Services.IAccountService, Services.AccountService>();

            // Register Job
            var typeJobs = referencedAssemblies
               .SelectMany(a => a.DefinedTypes)
            .Select(type => type.AsType())
               .Where(x => x != typeof(Job.IJob) && typeof(Job.IJob).IsAssignableFrom(x)).ToArray();
            if (types != null && types.Length > 0)
            {
                var implementJobs = typeJobs.Where(x => x.IsClass).ToArray();
                foreach (var implementType in implementJobs)
                {
                    services.AddScoped(implementType);
                }
            }

            return services;
        }

        /// <summary>
        /// AddHangfireJob
        /// </summary>
        /// <param name="serviceProvider"></param>
        private static void AddHangfireJob(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var baseType = typeof(Core.Job.IJob);
            var path = AppDomain.CurrentDomain.RelativeSearchPath ?? AppDomain.CurrentDomain.BaseDirectory;
            var referencedAssemblies = System.IO.Directory.GetFiles(path, "ModernWMS*.dll").Select(Assembly.LoadFrom).ToArray();
            var types = referencedAssemblies
                .SelectMany(a => a.DefinedTypes)
                .Select(type => type.AsType())
                .Where(x => x != baseType && baseType.IsAssignableFrom(x)).ToArray();
            if (types != null && types.Length > 0)
            {
                var implementTypes = types.Where(x => x.IsClass).ToArray();
                foreach (var implementType in implementTypes)
                {
                    var job = scope.ServiceProvider.GetService(implementType) as Core.Job.IJob;
                    if (job != null)
                    {
                        Hangfire.RecurringJob.AddOrUpdate(() => job.Execute(), job.CronExpression, TimeZoneInfo.Local, "wms");
                    }
                }
            }
        }

        #endregion dynamic injection
    }
}
