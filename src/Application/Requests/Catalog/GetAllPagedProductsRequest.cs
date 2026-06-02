using BlazorHero.CleanArchitecture.Application.Enums;

namespace BlazorHero.CleanArchitecture.Application.Requests.Catalog
{
    public class GetAllPagedProductsRequest : PagedRequest
    {
        public string SearchString { get; set; }
        public ProductStatusFilter? StatusFilter { get; set; }
    }
}