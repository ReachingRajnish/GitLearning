using Apttus.Security.Common.Authentication.DTO.RequestContext;
using Apttus.Contracts.Common;
using Apttus.Contracts.DAL.Interfaces;
using Apttus.Contracts.Domain.Interfaces;
using Apttus.Contracts.Model.DTO;
using Apttus.Contracts.Model.Enums;
using Apttus.DataAccess.Common.Interface;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Apttus.DataAccess.Common.CustomTypes;
using Apttus.Metadata.Client.Interface;
using Apttus.Core.CommonObjects.Manager;
using Apttus.Contracts.Common.Interfaces;

namespace Apttus.Contracts.Domain {

    public class AgreementDocumentManager : IAgreementDocumentManager {
        internal IAgreementDocumentRepository agreementDocumentRepository { get; set; }
        internal IDocGenHelper docGenHelper { get; set; }
        internal IAgreementRepository agreementRepository { get; set; }
        internal IActivityManager activityManager { get; set; }
        internal IAgreementLifecycleHelper lifecycleHelper { get; set; }
        internal IDocumentVersionRepository documentVersionRepository { get; set; }
        internal IProductSettingHelper productSettingHelper { get; set; }
        internal ILogger log { get; set; }
        internal ApttusRequestContext apiContext { get; set; }
        internal IObjectMetadata objectMetadata { get; set; }

        private const string APTTUS_OBJECT_AGREEMENT = "clm_Agreement";
        private const string APTTUS_OBJECT_DOCUMENTOUTPUTFORMAT = "documentOutputFormat";
        private const string APTTUS_OBJECT_DOCUMENTPROTECTION = "documentProtection";

        private const string AGREEMENT_FIELD_STATUS_CATEGORY = "statusCategory";
        private const string AGREEMENT_FIELD_STATUS = "status";
        private const string DOCUMENTPROTECTION_FIELD_PROTECTION_LEVEL = "protectionLevel";
        private const string DOCUMENTPROTECTION_FIELD_PROTECTION_TYPE = "protectionType";
        private const string DOCUMENTVERSIONDETAIL_FIELD_PARENTID = "DocumentVersionId";
        private const string ATTACHMENT_FIELD_CONTEXTOBJECT = "ContextObject";
        private const string FIELD_ID = "Id";

        //Protection Levels
        private const int DOCUMENTPROTECTION_LEVEL_READ_ONLY = 100000004;

        private const int DOCUMENTPROTECTION_LEVEL_FULL_ACCESS = 100000000;

        public AgreementDocumentManager(ApttusRequestContext context) {
            apiContext = context;
        }

        /// <summary>
        /// Creates the document.
        /// </summary>
        /// <param name="agreementId">The agreement identifier.</param>
        /// <param name="isVersioned">if set to <c>true</c> [is versioned].</param>
        /// <param name="filename">The filename.</param>
        /// <param name="contentType">Type of the content.</param>
        /// <param name="fileContents">The file contents.</param>
        /// <param name="notes">Notes</param>
        /// <param name="isImportOffline">if set to <c>true</c> [is import offline].</param>
        /// <param name="documentSecurity">The security level of the document</param>
        /// <returns></returns>
        public async Task<string> CreateDocument(string agreementId, bool isVersioned, string filename, string contentType, byte[] fileContents, string notes, bool isImportOffline, SelectOption documentType = null, SelectOption documentSecurity = null) {
            var attachmentId = await agreementDocumentRepository.CreateDocument(agreementId, isVersioned, filename, contentType, fileContents, notes, documentType, documentSecurity);

            if(isImportOffline) {
                await ImportOfflineDoc(agreementId, attachmentId);
            }
            return attachmentId;
        }


        /// <summary>
        /// Gets the documents.
        /// </summary>
        /// <param name="agreementId">The agreement identifier.</param>
        /// <returns></returns>
        public async Task<List<DocumentStoreFileInfo>> GetDocuments(string agreementId) {
            var documents = await agreementDocumentRepository.GetDocuments(agreementId);

            if(documents != null) {
                var documentDownloadUrl = await productSettingHelper.GetAsync<string>("DocumentDownloadUrl");
                for(int i = 0; i < documents.Count; i++) {
                    var docFileInfo = documents[i];
                    docFileInfo.DeepLinkUrl = DeepLinkUrlBuilder.GetDeepLinkUrl(docFileInfo.FileId, apiContext.TenantConfig.AppHost, documentDownloadUrl);
                }
            }

            return documents;
        }

        /// <summary>
        /// Gets latest documents.
        /// </summary>
        /// <param name="agreementId">The agreement identifier.</param>
        /// <returns></returns>
        public async Task<List<DocumentVersionInfo>> GetLatestDocuments(string agreementId) {
            var documents = await agreementDocumentRepository.GetLatestDocuments(agreementId);

            if(documents != null) {
                var documentDownloadUrl = await productSettingHelper.GetAsync<string>("DocumentDownloadUrl");
                for(int i = 0; i < documents.Count; i++) {
                    var docFileInfo = documents[i];
                    docFileInfo.LatestDocInfo.DeepLinkUrl = DeepLinkUrlBuilder.GetDeepLinkUrl(docFileInfo.LatestDocInfo.FileId, apiContext.RequestUrl, documentDownloadUrl);
                }
            }

            return documents;
        }

        /// <summary>
        /// Gets Document specific to a version
        /// </summary>
        /// <param name="documentVersionId"></param>
        /// <returns></returns>
        public async Task<List<DocumentStoreFileInfo>> GetDocumentsByVersionId(string documentVersionId) {
            var documents = await agreementDocumentRepository.GetDocumentsByVersionIds(new List<string> { documentVersionId });

            if(documents != null) {
                var documentDownloadUrl = await productSettingHelper.GetAsync<string>("DocumentDownloadUrl");
                for(int i = 0; i < documents.Count; i++) {
                    var docFileInfo = documents[i];
                    docFileInfo.DeepLinkUrl = DeepLinkUrlBuilder.GetDeepLinkUrl(docFileInfo.FileId, apiContext.RequestUrl, documentDownloadUrl);
                }
            }

            return documents;
        }

        /// <summary>
        /// Deletes Documents By Versionids
        /// </summary>
        /// <param name="documentVersionIds">List of document version ids to be deleted</param>
        /// <returns></returns>
        public async Task<bool> DeleteDocumentsByVersionId(List<string> documentVersionIds) {
            var documents = await documentVersionRepository.GetDocumentVersionDetailsByParentId(documentVersionIds.ToArray(), null);
            if(documents != null && documents.Count > 0) {
                var dvds = documents.Select(x => x.GetId()).ToList();
                //Delete Documents
                await agreementDocumentRepository.DeleteDocumentsByParentIds(dvds);
                //Delete Document Version Details
                await documentVersionRepository.DeleteDocumentVersionDetails(dvds);
                //Delete Document Version
                await documentVersionRepository.DeleteDocumentVersions(documentVersionIds);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Deletes Documents by fileids and dvds
        /// </summary>
        /// <param name="fileIds">List of fileids to be deleted</param>
        /// <param name="dvds">List of dvds to be deleted</param>
        /// <returns></returns>
        public async Task<bool> DeleteDocuments(string[] fileIds, string[] dvds) {
            if(fileIds != null && fileIds.Length > 0) {
                string typeParentid = ATTACHMENT_FIELD_CONTEXTOBJECT + "." + FIELD_ID;
                //If no dvds provided then get from storage
                if(dvds == null || dvds.Length == 0) {
                    dvds = (await agreementDocumentRepository.GetDocumentsById(fileIds.ToList(), new List<string> { ATTACHMENT_FIELD_CONTEXTOBJECT }))
                        .Select(x => x.GetValue<string>(typeParentid)).ToArray();
                }
                if(dvds != null && dvds.Length > 0) {
                    var dvdInstances = await documentVersionRepository.GetDocumentVersionDetails(dvds);
                    var documentversionIds = dvdInstances.Select(x => x.GetValue<string>($"{DOCUMENTVERSIONDETAIL_FIELD_PARENTID}.{FIELD_ID}")).Distinct().ToList();
                    //Delete Files
                    await agreementDocumentRepository.DeleteDocumentsByParentIds(dvds.ToList());

                    //Delete Document Version Details
                    await documentVersionRepository.DeleteDocumentVersionDetails(dvds.ToList());

                    //Delete Document Version
                    await DeleteDocumentVersion(dvds, documentversionIds);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Updates the document.
        /// </summary>
        /// <param name="fileInfo">The file information.</param>
        /// <returns></returns>
        public async Task<bool> UpdateDocument(DocumentStoreFileInfo fileInfo) {
            await documentVersionRepository.UpdateDocumentType(fileInfo.ParentId, fileInfo.DocumentType);
            if(fileInfo.DocumentSecurity != null) {
                await documentVersionRepository.UpdateDocumentSecurityAsync(fileInfo.ParentId, fileInfo.DocumentSecurity);
            }
            return await agreementDocumentRepository.UpdateNotes(fileInfo.FileId, fileInfo.Notes);
        }

        /// <summary>
        /// Gets File Content from fileid
        /// </summary>
        /// <param name="id">FileId</param>
        /// <returns></returns>
        public async Task<DocumentStoreFileInfo> GetDocumentContent(string id) {
            return await agreementDocumentRepository.GetDocumentContent(id);
        }

        /// <summary>
        /// Get Agreement Document Protection Options
        /// </summary>
        /// <param name="action">Document Action</param>
        /// <returns>Protection Levels along with the ProtectionType</returns>
        public async Task<DocumentProtectionDTO> GetDocumentProtection(DocumentAction action) {
            DocumentProtectionDTO documentProtection = new DocumentProtectionDTO() {
                ProtectionLevels = new Dictionary<string, string>()
            };
            string nonCanonicalProtectionLevel = $"{APTTUS_OBJECT_DOCUMENTPROTECTION}.{DOCUMENTPROTECTION_FIELD_PROTECTION_LEVEL}";
            var documentProtectionMetadata = await objectMetadata.GetAsync(APTTUS_OBJECT_DOCUMENTPROTECTION);
            var protectionLevelMetadata = documentProtectionMetadata.Fields[nonCanonicalProtectionLevel];
            var availableProtectionLevels = protectionLevelMetadata.SelectOptionSet.SelectOptions;

            List<IObjectInstance> docProtectionRecords = await agreementDocumentRepository.GetDocumentProtection(action);
            if(docProtectionRecords != null && docProtectionRecords.Count > 0) {
                foreach(IObjectInstance docProtectionRecord in docProtectionRecords) {
                    int protectionType = docProtectionRecord.GetValue<int>($"{APTTUS_OBJECT_DOCUMENTPROTECTION}.{DOCUMENTPROTECTION_FIELD_PROTECTION_TYPE}");
                    if(protectionType == default(int)) {
                        continue;
                    }

                    if((ProtectionType)protectionType == ProtectionType.UNPROTECT) {
                        documentProtection.ProtectionType = Convert.ToString(ProtectionType.UNPROTECT);
                        documentProtection.ProtectionLevels.Clear();
                        documentProtection.ProtectionLevels.Add(Convert.ToString((int)GetProtectionLevel(DOCUMENTPROTECTION_LEVEL_FULL_ACCESS)), availableProtectionLevels.ToList().Where(options => options.OptionKey.Equals(Convert.ToString(DOCUMENTPROTECTION_LEVEL_FULL_ACCESS))).FirstOrDefault().OptionValue);
                        break;
                    } else if((ProtectionType)protectionType == ProtectionType.PROMPT) {
                        documentProtection.ProtectionType = Convert.ToString(ProtectionType.PROMPT);
                        documentProtection.ProtectionLevels.Clear();
                        foreach(var options in availableProtectionLevels) {
                            documentProtection.ProtectionLevels.Add(Convert.ToString((int)GetProtectionLevel(Convert.ToInt32(options.OptionKey))), availableProtectionLevels.ToList().Where(option => option.OptionKey.Equals(options.OptionKey)).FirstOrDefault().OptionValue);
                        }
                        break;
                    } else if((ProtectionType)protectionType == ProtectionType.AUTOMATIC) {
                        documentProtection.ProtectionType = Convert.ToString(ProtectionType.AUTOMATIC);
                        int protectionLevelKey = docProtectionRecord.GetValue<int>(nonCanonicalProtectionLevel);
                        if(protectionLevelKey != default(int) && !documentProtection.ProtectionLevels.ContainsKey(Convert.ToString((int)GetProtectionLevel(protectionLevelKey)))) {
                            documentProtection.ProtectionLevels.Add(Convert.ToString((int)GetProtectionLevel(protectionLevelKey)), availableProtectionLevels.ToList().Where(options => options.OptionKey.Equals(Convert.ToString(protectionLevelKey))).FirstOrDefault().OptionValue);
                        }
                    }
                }
            }

            if(documentProtection.ProtectionLevels.Count == 0) {
                documentProtection.ProtectionLevels.Add(Convert.ToString((int)GetProtectionLevel(DOCUMENTPROTECTION_LEVEL_READ_ONLY)), availableProtectionLevels.ToList().Where(options => options.OptionKey.Equals(Convert.ToString(DOCUMENTPROTECTION_LEVEL_READ_ONLY))).FirstOrDefault().OptionValue);
                documentProtection.ProtectionType = Convert.ToString(ProtectionType.IGNORE);
            }

            return documentProtection;
        }

        /// <summary>
        /// Get Agreement Document Ouput Formats
        /// </summary>
        /// <param name="recordTypeId">Record type of the Agreement</param>
        /// <returns>OutputFormats along with the Watermark information</returns>
        public async Task<DocumentOutputFormatDTO> GetDocumentOutputFormats(Guid recordTypeId) {
            DocumentOutputFormatDTO documentOutputFormat = new DocumentOutputFormatDTO();
            string nonCanonicalOutputFormat = $"{APTTUS_OBJECT_DOCUMENTOUTPUTFORMAT}.outputFormat";
            string nonCanonicalAllowOverride = $"{APTTUS_OBJECT_DOCUMENTOUTPUTFORMAT}.allowOverride";
            string nonCanonicalIncludeWatermark = $"{APTTUS_OBJECT_DOCUMENTOUTPUTFORMAT}.includeWatermark";
            string nonCanonicalAllowOverrideWatermark = $"{APTTUS_OBJECT_DOCUMENTOUTPUTFORMAT}.allowOverrideWatermark";
            var documentOutputFormatMetadata = await objectMetadata.GetAsync(APTTUS_OBJECT_DOCUMENTOUTPUTFORMAT);
            var outputFormatFieldMetadata = documentOutputFormatMetadata.Fields[nonCanonicalOutputFormat];
            documentOutputFormat.OutputFormats = new Dictionary<string, string>();
            outputFormatFieldMetadata.SelectOptionSet.SelectOptions.ForEach(options => documentOutputFormat.OutputFormats.Add(options.OptionKey, options.OptionValue));

            List<IObjectInstance> docOutputFormatRecords = await agreementDocumentRepository.GetDocumentOutputFormats(recordTypeId);
            if(docOutputFormatRecords != null && docOutputFormatRecords.Count > 0) {
                foreach(IObjectInstance docOutputFormatRecord in docOutputFormatRecords) {
                    if(!documentOutputFormat.OverrideOutputFormat) {
                        documentOutputFormat.OverrideOutputFormat = docOutputFormatRecord.GetValue<bool>(nonCanonicalAllowOverride);
                    }
                    if(!documentOutputFormat.IncludeWatermark) {
                        documentOutputFormat.IncludeWatermark = docOutputFormatRecord.GetValue<bool>(nonCanonicalIncludeWatermark);
                    }
                    if(!documentOutputFormat.OverrideWatermark) {
                        documentOutputFormat.OverrideWatermark = docOutputFormatRecord.GetValue<bool>(nonCanonicalAllowOverrideWatermark);
                    }
                    if(docOutputFormatRecord.GetValue<int>(nonCanonicalOutputFormat) != default(int)) {
                        documentOutputFormat.FormatToSelect = Convert.ToString(docOutputFormatRecord.GetValue<int>(nonCanonicalOutputFormat));
                    }
                }
            } else {
                documentOutputFormat.FormatToSelect = Convert.ToString((int)DocumentOutputFormat.DOCX);
            }

            return documentOutputFormat;
        }

        /// <summary>
        /// Gets document version details by dvd
        /// </summary>
        /// <param name="dvdId">dvd</param>
        /// <returns></returns>
        public async Task<IObjectInstance> GetAgreementByDocumentVersionDetail(string dvdId) {
            return await agreementDocumentRepository.GetAgreementByDocumentVersionDetail(dvdId);
        }

        #region Private methods

        private async Task ImportOfflineDoc(string agreementId, string attachmentId) {
            var protectionToApply = await GetProtectionLevelForImportOffline();
            await docGenHelper.ImportOfflineDocument(agreementId, attachmentId, protectionToApply);
            await SetAgreementStatusToInAuthoring(agreementId);
            await activityManager.Create(new Core.CommonObjects.Model.Activity {
                Description = "Document imported offline",
                Name = GlobalConstants.CLM_ACTIVITY_NAME,
                ActivityDate = DateTime.UtcNow,
                ContextObject = new Composite { Id = agreementId, Type = APTTUS_OBJECT_AGREEMENT }
            });
        }

        private async Task<DocGen.Model.Enum.DocumentProtectionLevel> GetProtectionLevelForImportOffline() {
            var protectionToApply = DocGen.Model.Enum.DocumentProtectionLevel.NoProtection;
            var docProtection = await docGenHelper.GetDocumentProtection(DocGen.Model.Enum.DocumentAction.ImportOfflineDocument);
            if(docProtection != null && !string.IsNullOrEmpty(docProtection.ProtectionType) && docProtection.ProtectionType.Equals("AUTOMATIC") && docProtection.ProtectionLevels != null && docProtection.ProtectionLevels.Count > 0) {
                protectionToApply = (DocGen.Model.Enum.DocumentProtectionLevel)Convert.ToInt32(docProtection.ProtectionLevels?.ElementAt(0).Key);
            }
            return protectionToApply;
        }

        private ProtectionLevel GetProtectionLevel(int protectionLevelId) {
            ProtectionLevel protectionLevelToReturn;
            switch(protectionLevelId) {
                case 100000000:
                    protectionLevelToReturn = ProtectionLevel.NoProtection;
                    break;

                case 100000001:
                    protectionLevelToReturn = ProtectionLevel.AllowOnlyRevisions;
                    break;

                case 100000002:
                    protectionLevelToReturn = ProtectionLevel.AllowOnlyComments;
                    break;

                case 100000003:
                    protectionLevelToReturn = ProtectionLevel.AllowOnlyFormFields;
                    break;

                case 100000004:
                    protectionLevelToReturn = ProtectionLevel.AllowOnlyReading;
                    break;

                default:
                    protectionLevelToReturn = ProtectionLevel.AllowOnlyReading;
                    break;
            }
            return protectionLevelToReturn;
        }

        private async Task SetAgreementStatusToInAuthoring(string agreementId) {
            var agreement = await agreementRepository.GetAgreement(agreementId, new List<string> { AGREEMENT_FIELD_STATUS });

            var agreementState = agreement?.GetValue<SelectOption>(AGREEMENT_FIELD_STATUS);

            if(agreementState != null &&
                (agreementState.Key == lifecycleHelper.GetStatusInfoForAction(AgreementLifecycleAction.LIFECYCLE_ACTION_CREATE).Item2.Key ||
                 agreementState.Key == lifecycleHelper.GetStatusInfoForAction(AgreementLifecycleAction.LIFECYCLE_ACTION_AMEND).Item2.Key ||
                 agreementState.Key == lifecycleHelper.GetStatusInfoForAction(AgreementLifecycleAction.LIFECYCLE_ACTION_RENEW).Item2.Key)) {
                await agreementRepository.UpdateAgreementStatus(agreement, lifecycleHelper.GetStatusInfoForAction(AgreementLifecycleAction.LIFECYCLE_ACTION_GENERATE_DOC).Item1,
                                                            lifecycleHelper.GetStatusInfoForAction(AgreementLifecycleAction.LIFECYCLE_ACTION_GENERATE_DOC).Item2);
                await activityManager.Create(new Core.CommonObjects.Model.Activity {
                    Description = "Status updated to In Authoring",
                    Name = GlobalConstants.CLM_ACTIVITY_NAME,
                    ActivityDate = DateTime.UtcNow,
                    ContextObject = new Composite { Id = agreementId, Type = APTTUS_OBJECT_AGREEMENT }
                });
            }
        }

        private async Task DeleteDocumentVersion(string[] dvds, List<string> documentversionIds) {
            var referreddvds = await documentVersionRepository.GetDocumentVersionDetailsByParentId(documentversionIds.ToArray(), new List<string> { DOCUMENTVERSIONDETAIL_FIELD_PARENTID });
            //Remove referenced document versions
            if(referreddvds != null && referreddvds.Count > 0) {
                var refdocVersions = referreddvds.Select(x => x.GetValue<string>($"{DOCUMENTVERSIONDETAIL_FIELD_PARENTID}.{FIELD_ID}")).Distinct().ToList();
                refdocVersions.ForEach(x => documentversionIds.Remove(x));
            }

            if(documentversionIds.Any()) {
                await documentVersionRepository.DeleteDocumentVersions(documentversionIds);
            }
        }

        #endregion
    }
}
