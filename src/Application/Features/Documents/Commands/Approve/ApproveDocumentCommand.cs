using BlazorHero.CleanArchitecture.Application.Interfaces.Repositories;
using BlazorHero.CleanArchitecture.Application.Interfaces.Services;
using BlazorHero.CleanArchitecture.Domain.Entities.Misc;
using BlazorHero.CleanArchitecture.Domain.Enums;
using BlazorHero.CleanArchitecture.Shared.Wrapper;
using MediatR;
using Microsoft.Extensions.Localization;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.Approve
{
    public class ApproveDocumentCommand : IRequest<Result<int>>
    {
        public int Id { get; set; }
    }

    internal class ApproveDocumentCommandHandler : IRequestHandler<ApproveDocumentCommand, Result<int>>
    {
        private readonly IUnitOfWork<int> _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly IStringLocalizer<ApproveDocumentCommandHandler> _localizer;

        public ApproveDocumentCommandHandler(IUnitOfWork<int> unitOfWork, ICurrentUserService currentUserService, IStringLocalizer<ApproveDocumentCommandHandler> localizer)
        {
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _localizer = localizer;
        }

        public async Task<Result<int>> Handle(ApproveDocumentCommand command, CancellationToken cancellationToken)
        {
            var document = await _unitOfWork.Repository<Document>().GetByIdAsync(command.Id);
            if (document == null)
                return await Result<int>.FailAsync(_localizer["Document Not Found!"]);

            if (document.Status != DocumentStatus.PendingReview)
                return await Result<int>.FailAsync(_localizer["Only documents pending review can be approved."]);

            document.Status = DocumentStatus.Published;
            document.ReviewerId = _currentUserService.UserId;
            document.ReviewedOn = DateTime.UtcNow;
            document.RejectionReason = null;
            await _unitOfWork.Repository<Document>().UpdateAsync(document);
            await _unitOfWork.Commit(cancellationToken);
            return await Result<int>.SuccessAsync(document.Id, _localizer["Document approved and published."]);
        }
    }
}
