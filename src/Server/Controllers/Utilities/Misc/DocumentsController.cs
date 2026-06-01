using BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.AddEdit;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.Archive;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.Approve;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.Delete;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.Reject;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.SubmitForReview;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Queries.GetAll;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using BlazorHero.CleanArchitecture.Application.Features.Documents.Queries.GetById;
using BlazorHero.CleanArchitecture.Domain.Enums;
using BlazorHero.CleanArchitecture.Shared.Constants.Permission;
using Microsoft.AspNetCore.Authorization;

namespace BlazorHero.CleanArchitecture.Server.Controllers.Utilities.Misc
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentsController : BaseApiController<DocumentsController>
    {
        [Authorize(Policy = Permissions.Documents.View)]
        [HttpGet]
        public async Task<IActionResult> GetAll(int pageNumber, int pageSize, string searchString, DocumentStatus? statusFilter = null)
        {
            var docs = await _mediator.Send(new GetAllDocumentsQuery(pageNumber, pageSize, searchString, statusFilter));
            return Ok(docs);
        }

        [Authorize(Policy = Permissions.Documents.View)]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var document = await _mediator.Send(new GetDocumentByIdQuery { Id = id });
            return Ok(document);
        }

        [Authorize(Policy = Permissions.Documents.Create)]
        [HttpPost]
        public async Task<IActionResult> Post(AddEditDocumentCommand command)
        {
            return Ok(await _mediator.Send(command));
        }

        [Authorize(Policy = Permissions.Documents.Delete)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            return Ok(await _mediator.Send(new DeleteDocumentCommand { Id = id }));
        }

        [Authorize(Policy = Permissions.Documents.Edit)]
        [HttpPut("submit-for-review/{id}")]
        public async Task<IActionResult> SubmitForReview(int id)
        {
            return Ok(await _mediator.Send(new SubmitDocumentForReviewCommand { Id = id }));
        }

        [Authorize(Policy = Permissions.Documents.Approve)]
        [HttpPut("approve/{id}")]
        public async Task<IActionResult> Approve(int id)
        {
            return Ok(await _mediator.Send(new ApproveDocumentCommand { Id = id }));
        }

        [Authorize(Policy = Permissions.Documents.Reject)]
        [HttpPut("reject")]
        public async Task<IActionResult> Reject(RejectDocumentCommand command)
        {
            return Ok(await _mediator.Send(command));
        }

        [Authorize(Policy = Permissions.Documents.Archive)]
        [HttpPut("archive/{id}")]
        public async Task<IActionResult> Archive(int id)
        {
            return Ok(await _mediator.Send(new ArchiveDocumentCommand { Id = id }));
        }
    }
}