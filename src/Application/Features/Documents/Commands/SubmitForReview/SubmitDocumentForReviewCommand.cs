using BlazorHero.CleanArchitecture.Application.Interfaces.Repositories;
using BlazorHero.CleanArchitecture.Domain.Entities.Misc;
using BlazorHero.CleanArchitecture.Domain.Enums;
using BlazorHero.CleanArchitecture.Shared.Wrapper;
using MediatR;
using Microsoft.Extensions.Localization;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.SubmitForReview
{
    public class SubmitDocumentForReviewCommand : IRequest<Result<int>>
    {
        public int Id { get; set; }
    }

    internal class SubmitDocumentForReviewCommandHandler : IRequestHandler<SubmitDocumentForReviewCommand, Result<int>>
    {
        private readonly IUnitOfWork<int> _unitOfWork;
        private readonly IStringLocalizer<SubmitDocumentForReviewCommandHandler> _localizer;

        public SubmitDocumentForReviewCommandHandler(IUnitOfWork<int> unitOfWork, IStringLocalizer<SubmitDocumentForReviewCommandHandler> localizer)
        {
            _unitOfWork = unitOfWork;
            _localizer = localizer;
        }

        public async Task<Result<int>> Handle(SubmitDocumentForReviewCommand command, CancellationToken cancellationToken)
        {
            var document = await _unitOfWork.Repository<Document>().GetByIdAsync(command.Id);
            if (document == null)
                return await Result<int>.FailAsync(_localizer["Document Not Found!"]);

            if (document.Status != DocumentStatus.Draft && document.Status != DocumentStatus.Rejected)
                return await Result<int>.FailAsync(_localizer["Only Draft or Rejected documents can be submitted for review."]);

            document.Status = DocumentStatus.PendingReview;
            document.RejectionReason = null;
            await _unitOfWork.Repository<Document>().UpdateAsync(document);
            await _unitOfWork.Commit(cancellationToken);
            return await Result<int>.SuccessAsync(document.Id, _localizer["Document submitted for review."]);
        }
    }
}
