using BlazorHero.CleanArchitecture.Application.Interfaces.Repositories;
using BlazorHero.CleanArchitecture.Application.Interfaces.Services;
using BlazorHero.CleanArchitecture.Domain.Entities.Misc;
using BlazorHero.CleanArchitecture.Domain.Enums;
using BlazorHero.CleanArchitecture.Shared.Wrapper;
using MediatR;
using Microsoft.Extensions.Localization;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.Reject
{
    public class RejectDocumentCommand : IRequest<Result<int>>
    {
        public int Id { get; set; }
        [Required]
        public string RejectionReason { get; set; }
    }

    internal class RejectDocumentCommandHandler : IRequestHandler<RejectDocumentCommand, Result<int>>
    {
        private readonly IUnitOfWork<int> _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly IStringLocalizer<RejectDocumentCommandHandler> _localizer;

        public RejectDocumentCommandHandler(IUnitOfWork<int> unitOfWork, ICurrentUserService currentUserService, IStringLocalizer<RejectDocumentCommandHandler> localizer)
        {
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _localizer = localizer;
        }

        public async Task<Result<int>> Handle(RejectDocumentCommand command, CancellationToken cancellationToken)
        {
            var document = await _unitOfWork.Repository<Document>().GetByIdAsync(command.Id);
            if (document == null)
                return await Result<int>.FailAsync(_localizer["Document Not Found!"]);

            if (document.Status != DocumentStatus.PendingReview)
                return await Result<int>.FailAsync(_localizer["Only documents pending review can be rejected."]);

            document.Status = DocumentStatus.Rejected;
            document.ReviewerId = _currentUserService.UserId;
            document.ReviewedOn = DateTime.UtcNow;
            document.RejectionReason = command.RejectionReason;
            await _unitOfWork.Repository<Document>().UpdateAsync(document);
            await _unitOfWork.Commit(cancellationToken);
            return await Result<int>.SuccessAsync(document.Id, _localizer["Document rejected."]);
        }
    }
}
