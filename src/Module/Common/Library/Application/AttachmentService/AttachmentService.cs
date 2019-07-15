using System;
using System.IO;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Extensions.Options;
using Nm.Lib.Data.Abstractions;
using Nm.Lib.Utils.Core.Options;
using Nm.Lib.Utils.Core.Result;
using Nm.Module.Common.Application.AttachmentService.ResultModels;
using Nm.Module.Common.Application.AttachmentService.ViewModels;
using Nm.Module.Common.Domain.Attachment;
using Nm.Module.Common.Domain.Attachment.Models;
using Nm.Module.Common.Domain.AttachmentOwner;
using Nm.Module.Common.Domain.MediaType;
using Nm.Module.Common.Infrastructure.Repositories;
using FileInfo = Nm.Lib.Utils.Core.Files.FileInfo;

namespace Nm.Module.Common.Application.AttachmentService
{
    public class AttachmentService : IAttachmentService
    {
        private readonly ModuleCommonOptions _moduleCommonOptions;
        private readonly IMapper _mapper;
        private readonly IUnitOfWork _uow;
        private readonly IAttachmentRepository _repository;
        private readonly IAttachmentOwnerRepository _ownerRepository;
        private readonly IMediaTypeRepository _mediaTypeRepository;

        public AttachmentService(IAttachmentRepository repository, IUnitOfWork<CommonDbContext> uow, IAttachmentOwnerRepository ownerRepository, IOptionsMonitor<ModuleCommonOptions> moduleCommonOptionsMonitor, IMediaTypeRepository mediaTypeRepository, IMapper mapper)
        {
            _repository = repository;
            _uow = uow;
            _ownerRepository = ownerRepository;
            _moduleCommonOptions = moduleCommonOptionsMonitor.CurrentValue;
            _mediaTypeRepository = mediaTypeRepository;
            _mapper = mapper;
        }

        public async Task<IResultModel> Query(AttachmentQueryModel model)
        {
            var result = new QueryResultModel<AttachmentEntity>
            {
                Rows = await _repository.Query(model),
                Total = model.TotalCount
            };
            return ResultModel.Success(result);
        }

        public async Task<IResultModel<AttachmentUploadResultModel>> Upload(AttachmentUploadModel model, FileInfo fileInfo)
        {
            var result = new ResultModel<AttachmentUploadResultModel>();
            var entity = new AttachmentEntity
            {
                Module = model.Module,
                Group = model.Group,
                FileName = fileInfo.FileName,
                SaveName = fileInfo.SaveName,
                Ext = fileInfo.Ext,
                Md5 = fileInfo.Md5,
                Path = fileInfo.Path,
                FullPath = Path.Combine(fileInfo.Path, fileInfo.SaveName),
                Size = fileInfo.Size.Size,
                SizeCn = fileInfo.Size.ToString()
            };

            var mediaType = await _mediaTypeRepository.GetByExt(fileInfo.Ext);
            if (mediaType != null)
            {
                entity.MediaType = mediaType.Value;
            }

            _uow.BeginTransaction();

            if (await _repository.AddAsync(entity))
            {
                //如果需要授权访问附件，需要添加拥有者关联信息
                if (!model.Auth || await _ownerRepository.AddAsync(new AttachmentOwnerEntity { AttachmentId = entity.Id, AccountId = model.AccountId }))
                {
                    _uow.Commit();

                    var resultModel = _mapper.Map<AttachmentUploadResultModel>(entity);

                    return result.Success(resultModel);
                }
            }

            return result.Failed("上传失败");
        }

        public async Task<IResultModel<FileDownloadModel>> Download(Guid id, Guid accountId)
        {
            var result = new ResultModel<FileDownloadModel>();

            var attachment = await _repository.GetAsync(id);
            if (attachment == null)
                return result.Failed("附件不存在");

            if (attachment.Auth)
            {
                var has = await _ownerRepository.Exist(new AttachmentOwnerEntity { AccountId = accountId, AttachmentId = id });
                if (!has)
                {
                    return result.Failed("您无权访问该附件");
                }
            }

            var filePath = Path.Combine(_moduleCommonOptions.UploadPath, attachment.FullPath);
            if (!File.Exists(filePath))
                return result.Failed("文件不存在");

            return result.Success(new FileDownloadModel(filePath, attachment.FileName, attachment.MediaType));
        }
    }
}
