using BlazorHero.CleanArchitecture.Application.Enums;
using System.Linq;

namespace BlazorHero.CleanArchitecture.Client.Infrastructure.Routes
{
    public static class ProductsEndpoints
    {
        public static string GetAllPaged(int pageNumber, int pageSize, string searchString, string[] orderBy, ProductStatusFilter? statusFilter = null)
        {
            var url = $"api/v1/products?pageNumber={pageNumber}&pageSize={pageSize}&searchString={searchString}&orderBy=";
            if (orderBy?.Any() == true)
            {
                foreach (var orderByPart in orderBy)
                {
                    url += $"{orderByPart},";
                }
                url = url[..^1]; // loose training ,
            }
            if (statusFilter.HasValue)
            {
                url += $"&statusFilter={statusFilter.Value}";
            }
            return url;
        }

        public static string GetCount = "api/v1/products/count";

        public static string GetProductImage(int productId)
        {
            return $"api/v1/products/image/{productId}";
        }

        public static string ExportFiltered(string searchString, ProductStatusFilter? statusFilter = null)
        {
            var url = $"{Export}?searchString={searchString}";
            if (statusFilter.HasValue)
            {
                url += $"&statusFilter={statusFilter.Value}";
            }
            return url;
        }

        public static string Save = "api/v1/products";
        public static string Delete = "api/v1/products";
        public static string Export = "api/v1/products/export";
        public static string ChangePassword = "api/identity/account/changepassword";
        public static string UpdateProfile = "api/identity/account/updateprofile";
    }
}