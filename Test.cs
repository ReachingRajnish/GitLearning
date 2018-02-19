using Apttus.DocGen.Model.Enum;
using Apttus.LibraryDiagnostics;
using Apttus.Metadata.Client.Interface;
using Apttus.Metadata.Common.DTO.Runtime.V1;
using Apttus.Security.Common.Authentication.DTO.RequestContext;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Apttus.DocGen.Model.Constants.ErrorMessages;
using static Apttus.DocGen.Model.Constants.GlobalConstants;

namespace Apttus.DocGen.Domain.Implementations {

    public class DocGenManager : IDocGenManager {
        private const string OBJECT_TEMPOBJECT = "dgn_TempObject";
        private const string OBJECT_TEMPLATE = "dgn_Template";
        private const string OBJECT_DOCUMENTVERSION = "clm_DocumentVersion";
        private const string OBJECT_DOCUMENTVERSIONDETAIL = "clm_DocumentVersionDetail";

        private const string TEMPLATE_MERGEFIELDS = "MergefieldsInternal";
        private const string DOCX = ".docx";
        private const string PDF = ".pdf";
        private const string FIELD_DOCUMENTVERSIONID = "DocumentVersionId";
        private const string FIELD_REFERENCE = "Reference";
        private const string FIELD_TITLE = "Title";
        private const string FIELD_ID = "Id";
        private Dictionary<string, FieldMetadataDTO> m_EntityResponses = null;

        const string LOG_MESSAGE_DESCRIPTION_PREFIX = nameof(DocGenManager) + LOG_MESSAGE_SEGMENT_SEPARATOR;

        private readonly IDataRepository dataAccessRepository;
        private readonly IObjectMetadata objectMetadataMgr;
        private readonly IMergeServiceClient mergeServiceClient;
        private readonly ITemplateRepository templateRepository;
        private readonly IProductSettingUtil productSettingUtil;
        private readonly ILogger logger;
        private readonly ApttusRequestContext apiContext;
        private readonly IAsyncMergeCallUtil asyncMergeCallUtil;
        private readonly ITempObjectRepository tempObjectRepository;
        private readonly IMergeRequestUtil mergeRequestUtil;
        private readonly IAttachmentRepository attachmentRepository;
        private readonly IEmailUtil emailUtil;

        public DocGenManager(IDataRepository dataAccessRepository, IObjectMetadata objMetadataMgr, ApttusRequestContext apiContext,
            IMergeServiceClient mergeServiceClient, ITemplateRepository templateRepository, IProductSettingUtil productSettingUtil,
            IAsyncMergeCallUtil asyncMergeCallUtil, ITempObjectRepository tempObjectRepository, IMergeRequestUtil mergeRequestUtil,
            IAttachmentRepository attachmentRepository, IEmailUtil emailUtil) {
            this.dataAccessRepository = dataAccessRepository;
            objectMetadataMgr = objMetadataMgr;
            this.mergeServiceClient = mergeServiceClient;
            this.templateRepository = templateRepository;
            this.productSettingUtil = productSettingUtil;
            logger = PlatformLogger.Factory.CreateLogger<DocGenManager>();
            this.apiContext = apiContext;
            this.tempObjectRepository = tempObjectRepository;
            this.asyncMergeCallUtil = asyncMergeCallUtil;
            this.mergeRequestUtil = mergeRequestUtil;
            this.attachmentRepository = attachmentRepository;
            this.emailUtil = emailUtil;
        }

        /// <summary>
        /// Generates the document.
        /// </summary>
        /// <param name="docGenParams">The document gen parameters.</param>
        /// <returns></returns>
        public async Task<string> GenerateDocument(DocGenParamsDTO docGenParams) {
            ValidateDocumentGenerationParams(docGenParams);

            var dataPacket = await GetDataSourceXml(docGenParams, DocGenActions.GENERATE);
            return await ProcessMergeServiceRequest(dataPacket);
        }

        /// <summary>
        /// Regenerates the document.
        /// </summary>
        /// <param name="docGenParams">The document gen parameters.</param>
        /// <returns></returns>
        public async Task<string> RegenerateDoc(DocGenParamsDTO docGenParams) {
            ValidateDocumentGenerationParams(docGenParams);

            var dataPacket = await GetDataSourceXml(docGenParams, DocGenActions.REGENERATE);
            return await ProcessMergeServiceRequest(dataPacket);
        }

        /// <summary>
        /// Generates the supporting document.
        /// </summary>
        /// <param name="docGenParams">The document gen parameters.</param>
        /// <returns></returns>
        public async Task<string> GenerateSupportingDoc(DocGenParamsDTO docGenParams) {
            ValidateDocumentGenerationParams(docGenParams);

            var dataPacket = await GetDataSourceXml(docGenParams, DocGenActions.SUPPORTINGDOCGENERATE);
            return await ProcessMergeServiceRequest(dataPacket);
        }

        /// <summary>
        /// Previews the document.
        /// </summary>
        /// <param name="docGenParams">The document gen parameters.</param>
        /// <returns></returns>
        public async Task<string> PreviewDoc(DocGenParamsDTO docGenParams) {
            ValidateDocumentGenerationParams(docGenParams);

            var dataPacket = await GetDataSourceXml(docGenParams, DocGenActions.PREVIEW);
            return await ProcessMergeServiceRequest(dataPacket);
        }

        /// <summary>
        /// Updates the context object status.
        /// </summary>
        /// <param name="asyncCallId">The asynchronous call identifier.</param>
        /// <param name="attachmentid">Attachmentid of the generated document</param>
        /// <param name="errorMessage">The error message.</param>
        /// <returns></returns>
        public async Task UpdateContextObjectStatus(string asyncCallId, string attachmentId, string errorMessage) {
            await asyncMergeCallUtil.UpdateContextObjectStatusAsync(asyncCallId, attachmentId, errorMessage);
            var asyncMergeCall = await asyncMergeCallUtil.GetMergeCallAsync(asyncCallId);
            await emailUtil.SendEmailWithMergeStatus(asyncMergeCall, errorMessage);
        }

        private async Task<string> GetDataSourceXml(DocGenParamsDTO docGenParams, string action) {
            try {
                var time = System.Diagnostics.Stopwatch.StartNew();

                var objectId = Guid.Parse(docGenParams.ObjectId);

                var documentTemplate = await GetDocumentTemplate(docGenParams.TemplateId);
                var docGenRequestTemplateContent = documentTemplate?.GetValue<string>(TEMPLATE_MERGEFIELDS);
                if(string.IsNullOrWhiteSpace(docGenRequestTemplateContent)) {
                    throw GetExceptionObject($"{DOCGEN_SERVICE_TEMPLATE_MERGE_FIELDS_NULL_OR_EMPTY}");
                }

                var documentTemplateName = documentTemplate?.GetName();

                var documentGenerateRequest = GetMergeServiceRequest(docGenRequestTemplateContent);

                var isDocumentVersioned = IsDocumentVersioned(docGenParams.IsVersioned, action);
                if(isDocumentVersioned) {
                    await SetDocumentVersionDetails(docGenParams, documentGenerateRequest);
                } else {
                    await SetParentDetails(docGenParams, documentGenerateRequest, action);
                }

                documentGenerateRequest.Filename = await GetDocumentFileName(docGenParams, action, documentTemplateName, documentGenerateRequest.DocumentVersion);

                SetDocumentType(docGenParams, documentGenerateRequest);

                if(isDocumentVersioned) {
                    await UpdateDocumentTitle(docGenParams, documentGenerateRequest.Filename);
                }

                documentGenerateRequest.ActionName = MergeAction.MERGEDOCUMENT;
                documentGenerateRequest = await mergeRequestUtil.SetCommonRequestSpec(documentGenerateRequest);
                documentGenerateRequest.ClientId = apiContext.TenantConfig?.XACConfig?.AppKey;
                documentGenerateRequest.ObjectID = docGenParams.ObjectId;
                documentGenerateRequest.ObjectType = docGenParams.ObjectType;
                documentGenerateRequest.TemplateID = docGenParams.TemplateId;
                documentGenerateRequest.TemplateType = OBJECT_TEMPLATE;

                documentGenerateRequest.CallID = await asyncMergeCallUtil.InsertMergeCallAsync(docGenParams, action);

                documentGenerateRequest.ProtectionLevel = Convert.ToInt32(docGenParams.ProtectionLevel);
                documentGenerateRequest.IsDraft = docGenParams.IncludeWatermark;

                documentGenerateRequest.DocumentPassword = await GetDocumentPassword();
                documentGenerateRequest.PDFOwnerPassword = docGenParams.OwnerPassword;

                //// TODO: get the timezone key from user / organization
                documentGenerateRequest.TimeZoneKey = "";

                // Clear the header and footer details
                documentGenerateRequest.HeaderFieldText = "";
                documentGenerateRequest.FooterFieldDateFormat = "";

                //header / footer details according to configuration
                await SetDocumentHeaderFooter(documentGenerateRequest);

                documentGenerateRequest = await ProcessDataSourcePreparation(documentGenerateRequest, objectId);

                LogTrace(time.ElapsedMilliseconds);

                return ApttusXMLSerializer<MergeRequest>.Serialize(documentGenerateRequest);
            } catch(Exception ex) {
                LogError(ex);
                throw GetExceptionObject($"{EXCEPTION_SOURCE_IDENTIFIER_DOCGEN_SERVICE}", ex);
            }
        }

        private async Task<IObjectInstance> GetDocumentTemplate(string templateId) {
            var fields = new List<string> { TEMPLATE_MERGEFIELDS };
            var documentTemplates = await templateRepository.GetTemplatesById(new List<string> { templateId }, fields);
            return documentTemplates?.FirstOrDefault();
        }

        private MergeRequest GetMergeServiceRequest(string docGenRequestTemplateContent) {
            try {
                return ApttusXMLSerializer<MergeRequest>.Deserialize(docGenRequestTemplateContent);
            } catch(InvalidOperationException ex) {
                LogError(ex);
                throw GetExceptionObject($"{DOCGEN_SERVICE_TEMPLATE_INVALID_MERGE_FIELDS}", ex);
            }
        }

        private bool IsDocumentVersioned(bool isVersioned, string action) {
            return isVersioned && !IsPreviewDocument(action);
        }

        private async Task SetParentDetails(DocGenParamsDTO docGenParams, MergeRequest documentGenerateRequest, string action) {
            var isPreviewDocument = IsPreviewDocument(action);
            string tempObjectId = null;

            if(isPreviewDocument) {
                var tempObject = await tempObjectRepository.CreateTempObject(docGenParams);
                tempObjectId = tempObject?.GetId();
            }

            documentGenerateRequest.ParentID = isPreviewDocument ? tempObjectId : docGenParams.ObjectId;
            documentGenerateRequest.ParentType = isPreviewDocument ? OBJECT_TEMPOBJECT : docGenParams.ObjectType;
        }

        private bool IsPreviewDocument(string action) =>
            action.ToLowerInvariant() == DocGenActions.PREVIEW;

        private async Task<string> GetDocumentFileName(DocGenParamsDTO docGenParams, string action, string documentTemplateName, string documentVersion) {
            return string.IsNullOrWhiteSpace(docGenParams.OutputFileName) ?
                await GenerateDocFileName(docGenParams.ObjectId, docGenParams.ObjectType, documentTemplateName, action, documentVersion, DocumentVersionType.MAJOR)
              : docGenParams.OutputFileName;
        }

        private void SetDocumentType(DocGenParamsDTO docGenParams, MergeRequest documentGenerateRequest) {
            switch(docGenParams.OutputFormat) {
                case DocumentOutputFormat.DOCX:
                    documentGenerateRequest.OutputFormat = DocumentOutputFormat.DOCX;
                    documentGenerateRequest.Filename += DOCX;
                    break;

                case DocumentOutputFormat.PDF:
                    documentGenerateRequest.OutputFormat = DocumentOutputFormat.PDF;
                    documentGenerateRequest.Filename += PDF;
                    break;
            }
        }

        private async Task<string> GetDocumentPassword() {
            var documentPwdSettings = await productSettingUtil.GetAsync<string>("APTS_Password");
            return !string.IsNullOrEmpty(documentPwdSettings) ? documentPwdSettings : string.Empty;
        }

        private async Task SetDocumentHeaderFooter(MergeRequest documentGenerateRequest) {
            var shouldIncludeDocumentHeaderFooter = await ShouldIncludeDocumentHeaderFooter();
            if(shouldIncludeDocumentHeaderFooter) {
                documentGenerateRequest.HeaderFieldText = await GetDocumentHeaderFieldText();
                documentGenerateRequest.FooterFieldDateFormat = await GetDocumentFooterFieldText();
            }
        }

        private async Task<bool> ShouldIncludeDocumentHeaderFooter() {
            return await productSettingUtil.GetAsync<bool>("APTS_AppendHeaderFooter");
        }

        private async Task<string> GetDocumentHeaderFieldText() {
            return await productSettingUtil.GetAsync<string>("AgreementNumberFieldForImportedDocs");
        }

        private async Task<string> GetDocumentFooterFieldText() {
            return await productSettingUtil.GetAsync<string>("FooterDatetimeFormatForImportedDocs");
        }

        private async Task<MergeRequest> ProcessDataSourcePreparation(MergeRequest requestSpec, Guid objectID) {
            var time = System.Diagnostics.Stopwatch.StartNew();

            await ProcessMergeData(requestSpec, objectID);
            await ProcessRepeatDataSource(requestSpec.MergeData.RepeatSet, objectID);

            LogTrace(time.ElapsedMilliseconds);
            return requestSpec;
        }

        private async Task ProcessMergeData(MergeRequest requestSpec, Guid objectID) {
            try {
                var mergeDataQueryExpression = GetMergeDataQueryExpression(requestSpec, objectID);
                var mergeDataResult = await dataAccessRepository.GetRecordsAsync(mergeDataQueryExpression);
                var mergeDataResultList = mergeDataResult.ToList();
                if(mergeDataResultList.Count > 0) {
                    await SetMergeDataSpecificationValue(requestSpec.MergeData, mergeDataResultList[0]);
                }
            } catch(Exception ex) {
                LogError(ex);
                throw GetExceptionObject($"{DOCGEN_SERVICE_DATA_SOURCE_MERGE_DATA_ERROR}", ex);
            }
        }

        private async Task SetMergeDataSpecificationValue(MergeData mergeData, IObjectInstance mergeDataResult, bool isReferenceDataSet = false) {
            var time = System.Diagnostics.Stopwatch.StartNew();
            if(mergeData.Fields != null) {
                await MergeData(mergeData, mergeDataResult, isReferenceDataSet);
            }
            await MergeReferenceData(mergeData, mergeDataResult, isReferenceDataSet);
            LogTrace(time.ElapsedMilliseconds);
        }

        private async Task MergeReferenceData(MergeData mergeData, IObjectInstance mergeDataResult, bool isReferenceDataSet) {
            foreach(var dataSetSpec in mergeData.Lookups) {
                if(!isReferenceDataSet) {
                    await SetMergeDataSpecificationValue(dataSetSpec, mergeDataResult, true);
                } else {
                    if(mergeDataResult != null) {
                        await SetReferenceDataSpecificationValue(dataSetSpec, mergeDataResult);
                    }
                }
            }
        }

        private async Task MergeData(MergeData mergeData, IObjectInstance mergeDataResult, bool isReferenceDataSet) {
            var metadata = await objectMetadataMgr.GetAsync(mergeData.TypeName);
            foreach(var dataSpec in mergeData.Fields) {
                var attributeName = dataSpec.Name;

                if(isReferenceDataSet) {
                    attributeName = string.Concat(mergeDataResult.GetObjectType(), mergeData.Name, ".", dataSpec.Name);
                }
                dataSpec.Value = GetAttributeMetadataValue(metadata, mergeDataResult, attributeName);
            }
        }

        private async Task SetReferenceDataSpecificationValue(MergeData mergeData, IObjectInstance mergeDataResult) {
            var objectType = mergeDataResult.GetObjectType();
            var metadata = await objectMetadataMgr.GetAsync(objectType);
            foreach(var dataSpecification in mergeData.Fields) {
                dataSpecification.Value = GetAttributeMetadataValue(metadata, mergeDataResult, dataSpecification.Name);
            }
        }

        private Query GetMergeDataQueryExpression(MergeRequest requestSpec, Guid objectID) {
            var time = System.Diagnostics.Stopwatch.StartNew();
            var query = GetMergeDataDataSpecificationQueryExpression(requestSpec.MergeData);
            var objectNameField = requestSpec.MergeData.Fields.FirstOrDefault(dataSpec => dataSpec.Type == MergeDataType.IDField)?.Name.ToLower();

            var expr = new Expression(ExpressionOperator.AND);
            expr.AddCondition(objectNameField, FilterOperator.Equal, objectID);
            query.AddCriteria(expr);
            LogTrace(time.ElapsedMilliseconds);
            return query;

        }

        private async Task ProcessRepeatDataSource(List<MergeData> repeatSetSpecification, Guid objectID) {
            try {
                var time = System.Diagnostics.Stopwatch.StartNew();
                var mergeDataRepeatSetQueryExpressions = await GetMergeDataRepeatSetQueryExpression(repeatSetSpecification, objectID);

                //For each of the repeat query expression
                for(var i = mergeDataRepeatSetQueryExpressions.Count - 1; i >= 0; i--) {
                    var mergeDataRepeatSetQueryExpression = mergeDataRepeatSetQueryExpressions[i];

                    var mergeDataRepeatSetResult = await dataAccessRepository.GetRecordsAsync(mergeDataRepeatSetQueryExpression);
                    var mergeDataRepeatSetResultList = mergeDataRepeatSetResult.ToList();
                    var baseRepeatSetSpecification = repeatSetSpecification[i];

                    // For each row retrieved
                    foreach(IObjectInstance instance in mergeDataRepeatSetResultList) {
                        var repeatSetSpec = baseRepeatSetSpecification.Lookups[0].Clone();
                        await SetMergeDataSpecificationValue(repeatSetSpec, instance);

                        baseRepeatSetSpecification.Lookups.Add(repeatSetSpec);

                        // Process Repeat inside Repeat
                        var repeatIDFieldValue = Guid.Parse(repeatSetSpec.Fields.FirstOrDefault(dataSpec => dataSpec.Type == MergeDataType.IDField)?.Value);
                        await ProcessRepeatDataSource(repeatSetSpec.RepeatSet, repeatIDFieldValue);
                    }

                    repeatSetSpecification[i].Lookups.RemoveAt(0);

                    if(repeatSetSpecification[i].Lookups.Count == 0 &&
                        repeatSetSpecification[i].Fields.Count == 0 &&
                        repeatSetSpecification[i].RepeatSet.Count == 0) {
                        repeatSetSpecification.RemoveAt(i);
                    }
                }
                LogTrace(time.ElapsedMilliseconds);
            } catch(Exception ex) {
                LogError(ex);
                throw GetExceptionObject($"{DOCGEN_SERVICE_DATA_SOURCE_REPEAT_SET_DATA_ERROR}", ex);
            }
        }

        private async Task<List<Query>> GetMergeDataRepeatSetQueryExpression(List<MergeData> repeatSetSpecification, Guid objectID) {
            var time = System.Diagnostics.Stopwatch.StartNew();
            var queries = new List<Query>();

            foreach(var repeatSet in repeatSetSpecification) {
                if(repeatSet.Lookups.Count == 0)
                    continue;

                var query = GetMergeDataDataSpecificationQueryExpression(repeatSet.Lookups[0]);

                var parentObjectIdField = repeatSet.Name;

                if(string.IsNullOrEmpty(parentObjectIdField)) {
                    parentObjectIdField = await GetTargetIdFieldForObject(repeatSet.TypeName, repeatSet.ParentTypeName);
                }

                var filterExpr = new Expression(ExpressionOperator.AND);
                filterExpr.AddCondition(parentObjectIdField, FilterOperator.Equal, objectID);
                query.AddCriteria(filterExpr);

                queries.Add(query);
            }
            LogTrace(time.ElapsedMilliseconds);
            return queries;
        }

        private async Task<string> GetTargetIdFieldForObject(string sObjectName, string sTargetObject) {
            var time = System.Diagnostics.Stopwatch.StartNew();
            var sObjectResult = await GetEntityResponse(sObjectName);

            LogTrace(time.ElapsedMilliseconds);
            return sObjectResult.Name;
        }

        private async Task<FieldMetadataDTO> GetEntityResponse(string sEntityName) {
            var time = System.Diagnostics.Stopwatch.StartNew();
            if(m_EntityResponses == null)
                m_EntityResponses = new Dictionary<string, FieldMetadataDTO>();

            if(sEntityName.LastIndexOf("/", StringComparison.Ordinal) > 0)
                sEntityName = sEntityName.Substring(sEntityName.LastIndexOf("/", StringComparison.Ordinal) + 1);

            if(m_EntityResponses.ContainsKey(sEntityName)) {
                LogTrace(time.ElapsedMilliseconds);
                return m_EntityResponses[sEntityName];
            } else {
                var entityResponse = await objectMetadataMgr.GetAsync(sEntityName);

                var attrMeta = entityResponse.Fields[sEntityName];

                LogTrace(time.ElapsedMilliseconds);
                return attrMeta;
            }
        }

        private Query GetMergeDataDataSpecificationQueryExpression(MergeData dataSpecification) {
            var time = System.Diagnostics.Stopwatch.StartNew();
            var query = new Query(dataSpecification.TypeName);

            // fields of the mergeData
            query.AddColumns(dataSpecification.Fields.Select(dataSpec => dataSpec.Name.ToLower()).ToArray());

            if(dataSpecification.Fields == null)
                return query;

            // fields of the mergeData referenced object
            foreach(var dataSetSpec in dataSpecification.Lookups) {
                var linkToAttributeName = dataSetSpec.Fields.FirstOrDefault(dataSpec => dataSpec.Type == MergeDataType.IDField)?.Name.ToLower();
                var entityAlias = dataSpecification.TypeName + dataSetSpec.Name;

                var join = new Join(dataSpecification.TypeName, dataSetSpec.TypeName, dataSetSpec.Name.ToLower(),
                    linkToAttributeName, JoinType.LEFT, entityAlias);
                query.Columns.AddRange(dataSetSpec.Fields.Select(dataSpec => $"{entityAlias}.{dataSpec.Name.ToLower()}").ToList());

                query.AddJoin(join);
            }
            LogTrace(time.ElapsedMilliseconds);
            return query;
        }

        private async Task<string> GenerateDocFileName(string objectId, string objectType, string templateName, string lcActionName, string version, DocumentVersionType documentVersionType) {
            var docFileName = "%:Name%_%action%_%templatename%_%timestamp[MM-dd-yyyy]%_%vx%";

            var documentGenerationFileNameFormat = await GetDocumentGenerationFileNameFormat();
            if(!string.IsNullOrEmpty(documentGenerationFileNameFormat)) {
                docFileName = documentGenerationFileNameFormat;
            }

            docFileName = await BuildDocumentFileName(docFileName, lcActionName, objectId, objectType, templateName, version, documentVersionType);
            docFileName = SanitizeDocumentFileName(docFileName);

            return docFileName;
        }

        private async Task<string> GetDocumentGenerationFileNameFormat() {
            return await productSettingUtil.GetAsync<string>("DocumentGenerationFileNameFormat");
        }

        private async Task<string> BuildDocumentFileName(string docFileName, string actionName, string contextObjectId, string contextObjectType, string templateName, string version, DocumentVersionType documentVersionType) {
            var docNameTemplateContextObjectFieldValues = await GetDocumentNameTemplateContextObjectFieldValues(contextObjectId, contextObjectType, docFileName);

            ObjectMetadataDTO documentContextObjectMetadata = null;
            if(docNameTemplateContextObjectFieldValues != null) {
                documentContextObjectMetadata = await objectMetadataMgr.GetAsync(contextObjectType);
            }

            // populate the variables in the template
            foreach(var variable in GetDocumentNameTemplateVariables(docFileName)) {
                if(variable.StartsWith(":")) {
                    docFileName = docFileName.Replace("%" + variable + "%", (docNameTemplateContextObjectFieldValues == null ? string.Empty : GetAttributeMetadataValue(documentContextObjectMetadata, docNameTemplateContextObjectFieldValues, variable.Substring(1))));
                } else if(variable.Equals("action")) {
                    docFileName = docFileName.Replace("%" + variable + "%", actionName);
                } else if(variable.Equals("templatename")) {
                    docFileName = docFileName.Replace("%" + variable + "%", templateName);
                } else if(variable.StartsWith("timestamp")) {
                    var sDateTimeValue = string.Empty;
                    var serverTime = DateTime.UtcNow;

                    if(serverTime != DateTime.MinValue) {
                        try {
                            dynamic sDateTimeFormat = variable.Split('[', ']').ToList();
                            if(sDateTimeFormat.Count > 0)
                                sDateTimeFormat = sDateTimeFormat[1];
                            sDateTimeValue = serverTime.ToString(sDateTimeFormat);
                        } catch(Exception) {
                        }
                    }

                    docFileName = docFileName.Replace("%" + variable + "%", sDateTimeValue);
                } else if(variable.Equals("checkintype")) {
                    // checkin type
                    docFileName = docFileName.Replace("%" + variable + "%", GetFileSuffixForCheckinType(documentVersionType));
                } else if(variable.Equals("vx")) {
                    // checkin type
                    docFileName = docFileName.Replace("%" + variable + "%", version);
                }
            }
            return docFileName;
        }

        private string SanitizeDocumentFileName(string docFileName) {
            // limit file name to max chars
            docFileName = docFileName.Replace("__", "_");

            // replace any special character with space
            var re = new Regex("[;\\\\/:*?\"<>|&']");
            docFileName = re.Replace(docFileName, "");
            docFileName = (docFileName.Length > 255 ? docFileName.Substring(0, 255) : docFileName);

            return docFileName;
        }

        private string GetAttributeMetadataValue(ObjectMetadataDTO objectMetadata, IObjectInstance fieldValues, string attributeName) {
            try {
                var attr = GetObjectFieldMetadata(objectMetadata, attributeName);
                if(null == attr) return string.Empty;

                var valueToReturn = default(object);
                switch(attr.Type) {
                    case DataType.Bool:
                        valueToReturn = fieldValues.GetValue<bool>(attributeName);
                        break;

                    case DataType.Money:
                        valueToReturn = fieldValues.GetValue<decimal>(attributeName);
                        break;

                    case DataType.Date:
                        valueToReturn = fieldValues.GetValue<DateTime>(attributeName).ToString("MM/dd/yyyy");
                        break;

                    case DataType.DateTime:
                        valueToReturn = fieldValues.GetValue<DateTime>(attributeName).ToString("MM/dd/yyyy hh:mm:ss");
                        break;

                    case DataType.Decimal:
                        valueToReturn = fieldValues.GetValue<decimal>(attributeName);
                        break;

                    case DataType.UniqueIdentifier:
                        valueToReturn = fieldValues.GetValue<Guid>(attributeName);
                        break;

                    case DataType.Lookup:
                        valueToReturn = fieldValues.GetValue<Guid>($"{attributeName}.{FIELD_ID}");
                        break;

                    case DataType.Integer:
                        valueToReturn = fieldValues.GetValue<int>(attributeName);
                        break;

                    case DataType.MultiSelectOption:
                    case DataType.SelectOption:
                        valueToReturn = fieldValues.GetValue<SelectOption>(attributeName)?.Value ?? string.Empty;
                        break;

                    case DataType.String:
                    case DataType.LongString:
                        valueToReturn = fieldValues.GetValue<string>(attributeName);
                        break;

                    case DataType.Composite:
                        valueToReturn = fieldValues.GetValue<Composite>(attributeName)?.Id ?? string.Empty;
                        break;
                }
                return valueToReturn?.ToString();
            } catch(Exception ex) {
                LogError(ex);
                throw GetExceptionObject(string.Format(DOCGEN_SERVICE_FIELD_METADATA_VALUE_RETRIEVAL_ERROR, attributeName, objectMetadata?.Name), ex);
            }
        }

        private string GetFieldMetadataName(string attributeName) {
            if(attributeName.IndexOf('.') > -1) {
                return attributeName.Split('.')[1];
            }
            return attributeName;
        }

        private FieldMetadataDTO GetObjectFieldMetadata(ObjectMetadataDTO objectMetadata, string fieldName) {
            var fieldMetadataName = GetFieldMetadataName(fieldName);
            var fieldMetadata = objectMetadata?.Fields?.FirstOrDefault(x => x.Key.ToLower() == fieldMetadataName.ToLower());
            if(fieldMetadata == null || fieldMetadata.Equals(default(KeyValuePair<string, FieldMetadataDTO>))) return null;
            return fieldMetadata.Value.Value;
        }

        private async Task SetDocumentVersionDetails(DocGenParamsDTO docGenParams, MergeRequest documentGenerateRequest) {
            var documentVersionDetail = await GetVersionInfo(docGenParams.DVDId);
            documentGenerateRequest.ParentID = documentVersionDetail.GetId();
            documentGenerateRequest.ParentType = OBJECT_DOCUMENTVERSIONDETAIL;
            documentGenerateRequest.DocumentVersion = documentVersionDetail.GetName();
            documentGenerateRequest.DocumentVersionID = documentVersionDetail.GetId();
            documentGenerateRequest.DocumentGuid = documentVersionDetail.GetValue<string>($"{FIELD_DOCUMENTVERSIONID}.{FIELD_REFERENCE}");
        }

        private async Task UpdateDocumentTitle(DocGenParamsDTO docGenParams, string documentFileName) {
            var documentVersionDetail = await GetVersionInfo(docGenParams.DVDId);
            documentVersionDetail.SetValue(FIELD_TITLE, documentFileName);
            await dataAccessRepository.UpdateAsync(documentVersionDetail);
        }

        private async Task<IObjectInstance> GetVersionInfo(string dvdId) {
            var fields = new List<string> { $"{FIELD_DOCUMENTVERSIONID}.{FIELD_REFERENCE}", $"{FIELD_DOCUMENTVERSIONID}.{FIELD_TITLE}" };
            var documentVersionDetail = await dataAccessRepository.GetObjectByIdAsync(OBJECT_DOCUMENTVERSIONDETAIL, dvdId, fields);
            if(documentVersionDetail == null) {
                throw GetExceptionObject($"{DOCGEN_SERVICE_DOCUMENT_VERSION_NOT_EXISTS}");
            }

            return documentVersionDetail;
        }

        private async Task<IObjectInstance> GetDocumentNameTemplateContextObjectFieldValues(string contextObjectId, string contextObjectType, string docFileName) {
            if(string.IsNullOrEmpty(docFileName))
                return null;

            // populate the variables in the template
            var fields = GetDocumentNameTemplateContextObjectFields(docFileName);
            if(fields.Count == 0) return null;

            return await dataAccessRepository.GetObjectByIdAsync(contextObjectType, contextObjectId, fields);
        }

        private IEnumerable<string> GetDocumentNameTemplateVariables(string docNamingStr) {
            var regex = new Regex(@"([%%'])(\\?.)*?\1");
            var matches = regex.Matches(docNamingStr);

            return (from Match m in matches
                    select m.Value.Replace("%", string.Empty))
                    .ToList();
        }

        private List<string> GetDocumentNameTemplateContextObjectFields(string docFileName) {
            return (from variable in GetDocumentNameTemplateVariables(docFileName)
                    where variable.StartsWith(":")
                    select variable.Replace(":", string.Empty))
                          .ToList();
        }

        private string GetFileSuffixForCheckinType(DocumentVersionType documentVersionType) {
            var sFileSuffixForCheckInType = string.Empty;

            switch(documentVersionType) {
                case DocumentVersionType.MAJOR:

                    sFileSuffixForCheckInType = "Major";
                    break;

                case DocumentVersionType.MINOR:

                    sFileSuffixForCheckInType = "Negotiator";
                    break;

                case DocumentVersionType.REVISION:

                    sFileSuffixForCheckInType = "Reviewer";
                    break;
            }

            return sFileSuffixForCheckInType;
        }

        private void ValidateDocumentGenerationParams(DocGenParamsDTO documentGenerationParams) {
            if(documentGenerationParams == null) {
                throw new ArgumentNullException(nameof(documentGenerationParams), INPUT_NULL_OR_EMPTY);
            }

            if(string.IsNullOrWhiteSpace(documentGenerationParams.ObjectId)) {
                throw new ArgumentException(INPUT_NULL_OR_EMPTY, nameof(documentGenerationParams.ObjectId));
            }

            if(string.IsNullOrWhiteSpace(documentGenerationParams.ObjectType)) {
                throw new ArgumentException(INPUT_NULL_OR_EMPTY, nameof(documentGenerationParams.ObjectType));
            }

            if(string.IsNullOrWhiteSpace(documentGenerationParams.TemplateId)) {
                throw new ArgumentException(INPUT_NULL_OR_EMPTY, nameof(documentGenerationParams.TemplateId));
            }
        }

        #region Merge Service Methods

        private async Task<string> ProcessMergeServiceRequest(string requestPayload) {
            try {
                var response = await mergeServiceClient.SendAsync(requestPayload);
                return ProcessMergeServiceRespose(response);
            } catch(Exception ex) {
                LogError(ex);
                throw GetExceptionObject($"{EXCEPTION_SOURCE_IDENTIFIER_MERGE_SERVICE}", ex);
            }
        }

        private string ProcessMergeServiceRespose(string responseContent) {
            if(string.IsNullOrWhiteSpace(responseContent)) {
                throw GetExceptionObject($"{MERGE_SERVICE_RESPONSE_NULL_OR_EMPTY}");
            }

            var mergeServiceResponse = GetMergeServiceResponse(responseContent);
            if(mergeServiceResponse != null) {
                if(IsErrorMergeServiceResponse(mergeServiceResponse)) {
                    throw GetExceptionObject($"{MERGE_SERVICE_RESPONSE_ERROR} {mergeServiceResponse.Status?.Description}");
                }

                return mergeServiceResponse.Result?.Value ?? string.Empty;
            }
            return string.Empty;
        }

        private MergeServiceResponse GetMergeServiceResponse(string responseContent) {
            try {
                return ApttusXMLSerializer<MergeServiceResponse>.Deserialize(responseContent);
            } catch(InvalidOperationException ex) {
                LogError(ex);
                throw GetExceptionObject($"{MERGE_SERVICE_RESPONSE_INVALID} {EXCEPTION_MESSAGE_SEGMENT_SEPARATOR} {responseContent}", ex);
            }
        }

        private bool IsErrorMergeServiceResponse(MergeServiceResponse mergeServiceResponse) =>
            mergeServiceResponse.Status?.Code == MERGE_SERVICE_RESPONSE_ERROR_CODE;

        #endregion

        private void LogError(Exception exception, [CallerMemberName]string callerMemberName = null) =>
            logger.LogError($"{LOG_MESSAGE_DESCRIPTION_PREFIX}{callerMemberName}", exception);

        private void LogTrace(long methodExecutionTime, [CallerMemberName]string callerMemberName = null) =>
            logger.LogTrace($"{LOG_MESSAGE_DESCRIPTION_PREFIX}{callerMemberName}{LOG_MESSAGE_SEGMENT_SEPARATOR}{LOG_MESSAGE_SEGMENT_TOTALTIMETAKEN}{methodExecutionTime.ToString()}");

        private Exception GetExceptionObject(string exceptionMessage, Exception exception = null) =>
            new Exception($"{exceptionMessage} {EXCEPTION_MESSAGE_SEGMENT_SEPARATOR} {GetFullExceptionMessage(exception)}");

        private string GetFullExceptionMessage(Exception exception) =>
             exception != null ? ErrorHandler.ExtractErrorMessage(exception, string.Empty) : string.Empty;
    }
}
