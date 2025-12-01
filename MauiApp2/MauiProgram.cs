using MauiApp2;
using MauiApp2.Services;
// ... other using statements

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

        // Register AuthService
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddScoped<IBrandService, BrandService>();
        builder.Services.AddScoped<ICategoryService, CategoryService>();
        builder.Services.AddScoped<IProductService, ProductService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IRoleService, RoleService>();
        builder.Services.AddScoped<ITaxService, TaxService>();
        builder.Services.AddScoped<ISupplierService, SupplierService>();
        builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        builder.Services.AddScoped<IStockInService, StockInService>();
        builder.Services.AddScoped<IStockOutService, StockOutService>();
        builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();
        builder.Services.AddScoped<IReportService, ReportService>();
        builder.Services.AddScoped<IDatabaseSyncService, DatabaseSyncService>();
        builder.Services.AddScoped<IDatabaseSyncService, DatabaseSyncService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        return builder.Build();
    }
}