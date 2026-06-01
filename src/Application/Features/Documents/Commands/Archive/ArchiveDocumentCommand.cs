using BlazorHero.CleanArchitecture.Application.Interfaces.Repositories;
using BlazorHero.CleanArchitecture.Domain.Entities.Misc;
using BlazorHero.CleanArchitecture.Domain.Enums;
using BlazorHero.CleanArchitecture.Shared.Wrapper;
using MediatR;
using Microsoft.Extensions.Localization;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorHero.CleanArchitecture.Application.Features.Documents.Commands.Archive
{
    public class ArchiveDocumentCommand : IRequest<Result<int>>
    {
        public int Id { get; set; }
    }

    internal class ArchiveDocumentCommandHandler : IRequestHandler<ArchiveDocumentCommand, Result<int>>
    {
        private readonly IUnitOfWork<int> _unitOfWork;
        private readonly IStringLocalizer<ArchiveDocumentCommandHandler> _localizer;

        public ArchiveDocumentCommandHandler(IUnitOfWork<int> unitOfWork, IStringLocalizer<ArchiveDocumentCommandHandler> localizer)
        {
            _unitOfWork = unitOfWork;
            _localizer = localizer;
        }

        public async Task<Result<int>> Handle(ArchiveDocumentCommand command, CancellationToken cancellationToken)
        {
            var document = await _unitOfWork.Repository<Document>().GetByIdAsync(command.Id);
            if (document == null)
                return await Result<int>.FailAsync(_localizer["Document Not Found!"]);

            if (document.Status == DocumentStatus.Archived)
                return await Result<int>.FailAsync(_localizer["Document is already archived."]);

            document.Status = DocumentStatus.Archived;
            await _unitOfWork.Repository<Document>().UpdateAsync(document);
            await _unitOfWork.Commit(cancellationToken);
            return await Result<int>.SuccessAsync(document.Id, _localizer["Document archived."]);
        }
    }
}
