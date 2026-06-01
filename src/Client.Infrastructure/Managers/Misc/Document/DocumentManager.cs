using BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.AddEdit;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.Reject;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Queries.GetAll;
using BlazorHero.CleanArchitecture.Application.Requests.Documents;
using BlazorHero.CleanArchitecture.Client.Infrastructure.Extensions;
using BlazorHero.CleanArchitecture.Client.Infrastructure.Routes;
using BlazorHero.CleanArchitecture.Shared.Wrapper;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Queries.GetById;

namespace BlazorHero.CleanArchitecture.Client.Infrastructure.Managers.Misc.Document
{
    public class DocumentManager : IDocumentManager
    {
        private readonly HttpClient _httpClient;

        public DocumentManager(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IResult<int>> DeleteAsync(int id)
        {
            var response = await _httpClient.DeleteAsync($"{Routes.DocumentsEndpoints.Delete}/{id}");
            return await response.ToResult<int>();
        }

        public async Task<PaginatedResult<GetAllDocumentsResponse>> GetAllAsync(GetAllPagedDocumentsRequest request)
        {
            var statusFilter = request.StatusFilter.HasValue ? request.StatusFilter.Value.ToString() : "";
            var response = await _httpClient.GetAsync(DocumentsEndpoints.GetAllPaged(request.PageNumber, request.PageSize, request.SearchString, statusFilter));
            return await response.ToPaginatedResult<GetAllDocumentsResponse>();
        }

        public async Task<IResult<GetDocumentByIdResponse>> GetByIdAsync(GetDocumentByIdQuery request)
        {
            var response = await _httpClient.GetAsync(Routes.DocumentsEndpoints.GetById(request.Id));
            return await response.ToResult<GetDocumentByIdResponse>();
        }

        public async Task<IResult<int>> SaveAsync(AddEditDocumentCommand request)
        {
            var response = await _httpClient.PostAsJsonAsync(Routes.DocumentsEndpoints.Save, request);
            return await response.ToResult<int>();
        }

        public async Task<IResult<int>> SubmitForReviewAsync(int id)
        {
            var response = await _httpClient.PutAsync(DocumentsEndpoints.SubmitForReview(id), null);
            return await response.ToResult<int>();
        }

        public async Task<IResult<int>> ApproveAsync(int id)
        {
            var response = await _httpClient.PutAsync(DocumentsEndpoints.Approve(id), null);
            return await response.ToResult<int>();
        }

        public async Task<IResult<int>> RejectAsync(RejectDocumentCommand request)
        {
            var response = await _httpClient.PutAsJsonAsync(DocumentsEndpoints.Reject, request);
            return await response.ToResult<int>();
        }

        public async Task<IResult<int>> ArchiveAsync(int id)
        {
            var response = await _httpClient.PutAsync(DocumentsEndpoints.Archive(id), null);
            return await response.ToResult<int>();
        }
    }
}