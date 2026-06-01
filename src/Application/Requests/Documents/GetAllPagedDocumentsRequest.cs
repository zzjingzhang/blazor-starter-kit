using BlazorHero.CleanArchitecture.Domain.Enums;

namespace BlazorHero.CleanArchitecture.Application.Requests.Documents
{
    public class GetAllPagedDocumentsRequest : PagedRequest
    {
        public string SearchString { get; set; }
        public DocumentStatus? StatusFilter { get; set; }
    }
}