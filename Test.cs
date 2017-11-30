using Apttus.Contracts.Common;
using Apttus.Contracts.Common.Interfaces;
using Apttus.Contracts.DAL.AzureSQL;
using Apttus.Contracts.DAL.Interfaces;
using Apttus.Contracts.Domain.Interfaces;
using Apttus.Contracts.Domain.Util;
using Apttus.Contracts.Model.DTO;
using Apttus.Contracts.Model.Enums;
using Apttus.Contracts.Model.Model;
using Apttus.DataAccess.Common.CustomTypes;
using Apttus.DataAccess.Common.Interface;
using Apttus.LibraryDiagnostics;
using Apttus.Security.Common.Authentication.DTO.RequestContext;
using DocuSign.eSign.Api;
using DocuSign.eSign.Client;
using DocuSign.eSign.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Apttus.Callbacks.CLM.Contracts;

namespace Apttus.Contracts.Domain {
    public class DocuSignRESTServiceManager {
        internal ILogger log { get; set; }
        internal IAgreementRepository agreementRepo { get; set; }
        internal IAgreementDocumentRepository agreementDocRepo { get; set; }
        internal IDocuSignEnvelopeRepository envelopeRepo { get; set; }
        internal IDocuSignEnvelopeRecipientStatusRepository envelopeRecipientStatusRepo { get; set; }
        internal IDocumentVersionRepository documentVersionRepo { get; set; }
        internal IProductSettingHelper productSettingHelper { get; set; }
        internal IDocuSignUserRepository docuSignUserRepo { get; set; }
        internal ApttusRequestContext apiContext { get; set; }
        internal IAgreementDocumentManager agreementDocumentManager { get; set; }
        internal IDocuSignAttachmentSetupRepository attachmentSetupRepository { get; set; }
        internal IDocuSignNotificationParameterSetupRepository notificationSetupRepository { get; set; }
        internal IDocuSignDefaultRecipientRepository docuSignDefaultRecipientRepository { get; set; }
        internal IAttachmentRepository attachmentRepository { get; set; }
        internal IRelatedAgreementHelper relatedAgreementHelper { get; set; }
        internal ISignatureCallback signatureCallback { get; set; }

        private string useAccountDefaults = "true";
        private string reminderEnabled = "false";
        private string expireEnabled = "false";
        private EnvelopeDefinition envDef = new EnvelopeDefinition();
        private DocuSignUserDTO docuSignUserData = new DocuSignUserDTO();
        private EnvelopesApi docuSignEnvelopesApiClient;

        private string integratorKey = string.Empty;
        private string serverURL = string.Empty;

        private const string ENCRYPTED_INTEGRATOR_KEY = "7QPez5iTa9rYgzNn/9M2c6bMonHY0bKi5Zk9fnUOQHG0CYVO7F7uoZ70uVgURdtY";
        private const string OAUTH_TOKEN_URL = "/v2/oauth2/token";
        private const string APTTUS_OBJECT_DOCUSIGN_ENVELOPE = "docuSignEnvelope";
        private const string DOCUSIGNENVELOPE_FIELD_ENVELOPE_STATUS = "envelopeStatus";
        private const string DOCUSIGNENVELOPE_FIELD_PARENT_ID = "parentId";
        const string DOCUSIGNENVELOPE_FIELD_RELATEDAGREEMENTJSON = "RelatedAgreementJson";
        private const string REST_API_URL_PATH_EXTN = "/restapi";
        private const string APTTUS_OBJECT_DOCUSIGN_USER = "docuSignUser";
        private const string APTTUS_OBJECT_DOCUSIGN_RECIPIENTS = "docuSignRecipients";
        private const string DOCUSIGN_RECIPIENTS_FIELD_SIGNING_ORDER = "ImplicitSigningOrder";
        private const string DOCUSIGN_RECIPIENTS_FIELD_SIGNINGGROUPID = "SigningGroupId";
        private const string DOCUSIGN_RECIPIENTS_FIELD_SIGNINGGROUPNAME = "SigningGroupName";
        private const string DOCUSIGN_RECIPIENTS_FIELD_SIGNINGGROUPEMAIL = "SigningGroupEmail";
        private const string REFRESH_STATUS_ERROR_MESSAGE = "Please send it for eSignature and then hit Refresh Status to see the updated status";
        const string PREDEFINEDDOCUSIGNATTACHMENTS_FIELD_ISCOMBINESIGNEDDOCUMENTS = "IsCombineSignedDocuments";
        const string AGREEMENT_FIELD_STATUS = "status";

        const string DATA_OBJECT_FIELD_ID = "Id";
        const string DATA_OBJECT_FIELD_NAME = "Name";

        // Predefined voided reason - This will be captured from the UI once we have a mockup
        private const string DEFAULT_VOIDED_REASON = " has recalled this envelope.";

        public DocuSignRESTServiceManager(ApttusRequestContext context) {
            apiContext = context;
        }

        /// <summary>
        /// Initialize API client with oAuthToken
        /// </summary>
        private async Task InitializeClientUsingToken(string contextObjectId, string envelopeRecordId, string docuSignUserAccountId) {

            docuSignUserData = await docuSignUserRepo.Get(contextObjectId, envelopeRecordId, docuSignUserAccountId);
            var oAuthHeader = "bearer " + EncryptionUtil.DecryptStringAES(docuSignUserData.AccessTokenData.AccessToken, DocuSignConstants.KEY_PHRASE, DocuSignConstants.IV_PHRASE);

            // initialize client for desired environment
            var apiClient = new ApiClient(docuSignUserData.ServerInstanceUrl);
            Configuration cfg = new Configuration(apiClient);
            cfg.DefaultHeader.Clear();
            cfg.DefaultHeader.Remove(DocuSignConstants.OAUTH_HEADER);
            cfg.DefaultHeader.Add(DocuSignConstants.OAUTH_HEADER, oAuthHeader);

            //check if SOBO is enabled for the crm org, if yes, add the SOBO header to the DocuSign API calls
            // this will require the sender to be added to the DocuSign account being used for the eSignature transaction
            var productSettings = await productSettingHelper.GetAsync<string>(DocuSignConstants.DOCUSIGN_SOBO_SETTING);
            if(productSettings != null) {
                var isSoboEnabled = productSettings;
                if(!string.IsNullOrWhiteSpace(isSoboEnabled) && isSoboEnabled.Trim().ToLower().Equals("true")) {
                    var soboUser = apiContext.UserInfo.PrimaryEmail;
                    cfg.DefaultHeader.Add(DocuSignConstants.SOBO_HEADER_KEY, soboUser);
                }
            }
            docuSignEnvelopesApiClient = new EnvelopesApi(cfg);
        }

        /// <summary>
        /// Get the DocuSign Account Id for the supplied credentials
        /// </summary>
        /// <param name="apiUrl"></param>
        /// <param name="signupData"></param>
        /// <returns></returns>
        public async Task AuthenticateAndGetAccountId(string apiUrl, DocuSignAccountRegistrationDTO signupData) {

            // initialize client for desired environment
            var apiClient = new ApiClient(apiUrl); //for Demo
            Configuration cfg = new Configuration(apiClient);

            var authHeader = "{\"Username\":\"" + signupData.Username + "\", \"Password\":\"" + signupData.Password + "\", \"IntegratorKey\":\"" + integratorKey + "\"}";

            // remove the keys first to ensure that it doesn't throw duplicate key error 
            cfg.DefaultHeader.Clear();

            //add the headers
            cfg.AddDefaultHeader(DocuSignConstants.AUTH_HEADER_KEY, authHeader);
            cfg.AddDefaultHeader("Cache-Control", "no-cache, no-store, must-revalidate");
            cfg.AddDefaultHeader("Pragma", "no-cache");
            cfg.AddDefaultHeader("Expires", "0");

            var authApi = new AuthenticationApi(cfg);
            var loginInfo = authApi.Login();

            if(loginInfo.LoginAccounts != null && loginInfo.LoginAccounts.Count > 0) {

                //if the user has supplied the account id, use that to get the details from the list
                if(signupData.AccountId != null && loginInfo.LoginAccounts.Count > 1) {
                    foreach(LoginAccount account in loginInfo.LoginAccounts) {
                        if(account.AccountId.Equals(signupData.AccountId.Trim())) {
                            docuSignUserData.DocuSignAccountId = account.AccountId;
                            docuSignUserData.DocuSignBaseURL = account.BaseUrl;
                            //email is used for login
                            docuSignUserData.DocuSignUserEmail = account.Email;
                            docuSignUserData.DocuSignUserId = account.UserId;
                            docuSignUserData.DocuSignCompanyName = account.Name;
                            //username is the Name of the user as defined on the DocuSign account
                            docuSignUserData.DocuSignUserName = account.UserName;
                            break;
                        }
                    }

                } else {
                    // parse the first account ID that is returned
                    docuSignUserData.DocuSignAccountId = loginInfo.LoginAccounts[0].AccountId;
                    docuSignUserData.DocuSignBaseURL = loginInfo.LoginAccounts[0].BaseUrl;
                    //email is used for login
                    docuSignUserData.DocuSignUserEmail = loginInfo.LoginAccounts[0].Email;
                    docuSignUserData.DocuSignUserId = loginInfo.LoginAccounts[0].UserId;
                    docuSignUserData.DocuSignCompanyName = loginInfo.LoginAccounts[0].Name;
                    //username is the Name of the user as defined on the DocuSign account
                    docuSignUserData.DocuSignUserName = loginInfo.LoginAccounts[0].UserName;
                }
            }

            await GetAccessToken(signupData.Username, signupData.Password);
        }

        /// <summary>
        /// Registers the DocuSign account to make an entry on the DocuSign User entity
        /// </summary>
        /// <param name="signupData"></param>
        /// <returns>bool</returns>
        public async Task<string> RegisterAccount(DocuSignAccountRegistrationDTO signupData) {
            try {
                if(signupData.ServerInstanceUrl != null) {
                    serverURL = signupData.ServerInstanceUrl + REST_API_URL_PATH_EXTN;

                } else {
                    var productSettings = await productSettingHelper.GetAsync<string>(DocuSignConstants.DOCUSIGN_SERVER_INSTANCE_SETTING);
                    if(productSettings != null) {
                        serverURL = productSettings + REST_API_URL_PATH_EXTN;
                    }
                }

                integratorKey = EncryptionUtil.DecryptStringAES(ENCRYPTED_INTEGRATOR_KEY, DocuSignConstants.KEY_PHRASE, DocuSignConstants.IV_PHRASE);
                docuSignUserData.IsDefaultAdminAccount = signupData.IsDefaultAdminAccount;
                docuSignUserData.ServerInstanceUrl = serverURL;

                await AuthenticateAndGetAccountId(serverURL, signupData);
                return await docuSignUserRepo.Register(docuSignUserData);
            } catch(Exception ex) {
                log.LogError("Error registering the DocuSign user account:", ex);
                throw;
            }
        }

        /// <summary>
        /// Checks the most recent DocuSign envelope's status and updates the objects based on the response
        /// </summary>
        /// <param name="id"></param>
        /// <returns>string</returns>
        public async Task<string> GetEnvelopeStatus(Guid id) {
            var envelopeStatus = "success";
            try {
                var strParentId = id.ToString();
                var envelope = await envelopeRepo.GetEnvelopeByParentId(strParentId);
                if(envelope != null) {
                    var envelopeId = envelope.GetName();
                    var currentStatus = envelope.GetValue<string>(DOCUSIGNENVELOPE_FIELD_ENVELOPE_STATUS);
                    if(currentStatus.Equals(DocuSignConstants.ENVELOPE_STATUS_VOIDED) || currentStatus.Equals(DocuSignConstants.ENVELOPE_STATUS_VOIDED)) {
                        throw new ApplicationException(REFRESH_STATUS_ERROR_MESSAGE);
                    }

                    await ProcessEnvelopeStatus(envelopeId, envelope, strParentId, currentStatus, "GetEnvelopeStatus");
                } else {
                    throw new ApplicationException(REFRESH_STATUS_ERROR_MESSAGE);
                }
            } catch(Exception ex) {
                log.LogError("DocuSignRESTServiceManager:GetEnvelopeStatus:{0}", ex);
                throw;
            }
            return envelopeStatus;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="envelopeId"></param>
        public async Task ProcessEnvelopeStatusChange(string envelopeId) {
            try {
                var docuSignEnvelope = await envelopeRepo.GetEnvelopeRecordByEnvId(envelopeId);
                var currentStatus = docuSignEnvelope.GetValue<string>(DOCUSIGNENVELOPE_FIELD_ENVELOPE_STATUS);
                var parentId = docuSignEnvelope.GetValue<string>(DOCUSIGNENVELOPE_FIELD_PARENT_ID);

                await ProcessEnvelopeStatus(envelopeId, docuSignEnvelope, parentId, currentStatus, "ProcessEnvelopeStatusChange");
            } catch(Exception ex) {
                log.LogError("Error processing envelope status change:", ex);
                throw;
            }
        }

        private async Task ProcessEnvelopeStatus(string envelopeId, IObjectInstance envelopeRecord, string envelopeParentId, string currentStatus, string requestSource) {
            var requestName = $"DocuSignRESTServiceManager::{requestSource}";
            var envelopeRecordId = envelopeRecord.GetId();

            var dep = new AppInsightsDependency("EnvelopesApi::InitializeClientUsingToken", requestName, apiContext.RequestId);
            await InitializeClientUsingToken(envelopeParentId, envelopeRecordId, string.Empty);
            dep.End();

            dep = new AppInsightsDependency("EnvelopesApi::GetEnvelope", requestName, apiContext.RequestId);
            var envelope = docuSignEnvelopesApiClient.GetEnvelope(docuSignUserData.DocuSignAccountId, envelopeId);
            dep.End();

            dep = new AppInsightsDependency("EnvelopesApi::ListRecipents", requestName, apiContext.RequestId);
            var envRecipientsStatus = docuSignEnvelopesApiClient.ListRecipients(docuSignUserData.DocuSignAccountId, envelopeId);
            dep.End();

            await envelopeRecipientStatusRepo.SaveEnvelopeRecipientData(envelopeParentId, envelopeRecordId, envelopeId, envRecipientsStatus.ToJson());

            await envelopeRepo.UpdateEnvelopeStatus(envelopeId, envelope.Status, envelope.StatusChangedDateTime, currentStatus, envelopeRecordId);

            if(!envelope.Status.Equals(currentStatus, StringComparison.InvariantCultureIgnoreCase)) {
                var relatedAgreements = GetEnvelopeRelatedAgreements(envelopeRecord);
                var relatedAgreementIds = relatedAgreements?.Select(relatedAgreement => relatedAgreement.Id).ToList();

                if(envelope.Status.Equals(DocuSignConstants.ENVELOPE_STATUS_COMPLETED, StringComparison.InvariantCultureIgnoreCase)) {
                    await AddSignedDocument(envelopeId, envelopeParentId, relatedAgreementIds, requestSource);
                }
                await ProcessRelatedAgreements(envelopeParentId, envelope.Status, relatedAgreementIds);
            }
        }

        /// <summary>
        /// Creates the DocuSign envelope and sends it for eSignature
        /// </summary>
        /// <param name="envelopeData"></param>
        /// <param name="isFinalize"></param>
        /// <returns>string</returns>
        public async Task<string> CreateEnvelope(DocuSignEnvelopeDTO envelopeData, bool isFinalize, string returnUrl, string docuSignUserAccountId) {
            signatureCallback.PreDocusignSendForESignature(envelopeData, isFinalize, returnUrl, docuSignUserAccountId);
            string response = string.Empty;
            try {
                var parentId = envelopeData.parentid;

                var dep = new AppInsightsDependency("EnvelopesApi::InitializeClientUsingToken", "DocuSignRESTServiceManager::CreateEnvelope", apiContext.RequestId);
                await InitializeClientUsingToken(parentId, string.Empty, docuSignUserAccountId);
                dep.End();

                var envDef = await PrepareEnvelopeContent(envelopeData, isFinalize);

                dep = new AppInsightsDependency("EnvelopesApi::CreateEnvelope", "DocuSignRESTServiceManager::CreateEnvelope", apiContext.RequestId);
                var envelopeSummary = docuSignEnvelopesApiClient.CreateEnvelope(docuSignUserData.DocuSignAccountId, envDef);
                dep.End();

                List<RelatedAgreementJson> realtedAgreements = null;
                if(envelopeData.attachments != null && envelopeData.attachments.Count > 0) {
                    realtedAgreements = envelopeData.attachments
                        .Where(x => !(string.IsNullOrEmpty(x.ParentId) || x.ParentId == parentId))
                        .GroupBy(y => y.ParentId)
                        .Select(z => new RelatedAgreementJson { Id = z.Key, FileIds = z.Select(c => c.AttachmentId).ToList() }).ToList();
                }

                var envRecordId = await envelopeRepo.SaveEnvelopeData(parentId, envelopeSummary.EnvelopeId, envDef.ToJson(), envelopeSummary.Status, envelopeSummary.StatusDateTime, docuSignUserData.DocuSignUserCrmRecordId, JsonConvert.SerializeObject(realtedAgreements));
                if(envRecordId != null && !envRecordId.Equals(Guid.Empty)) {
                    if(realtedAgreements != null && realtedAgreements.Count > 0) {
                        await relatedAgreementHelper.UpdateRelatedAgreementStatus(SignatureUtil.GetSignatureStatusForDocusign(envelopeSummary.Status), realtedAgreements.Select(x => x.Id).ToList(), parentId);
                    }

                    dep = new AppInsightsDependency("EnvelopesApi::ListRecipients", "DocuSignRESTServiceManager::CreateEnvelope", apiContext.RequestId);
                    var envRecipientsStatus = docuSignEnvelopesApiClient.ListRecipients(docuSignUserData.DocuSignAccountId, envelopeSummary.EnvelopeId);
                    dep.End();

                    await envelopeRecipientStatusRepo.SaveEnvelopeRecipientData(parentId, envRecordId, envelopeSummary.EnvelopeId, envRecipientsStatus.ToJson());
                    response = "success";

                    if(isFinalize) {
                        var retUrl = new ReturnUrlRequest();
                        retUrl.ReturnUrl = returnUrl;
                        dep = new AppInsightsDependency("EnvelopesApi::CreateSenderView", "DocuSignRESTServiceManager::CreateEnvelope", apiContext.RequestId);
                        var url = docuSignEnvelopesApiClient.CreateSenderView(docuSignUserData.DocuSignAccountId, envelopeSummary.EnvelopeId, retUrl);
                        dep.End();
                        return url.Url;
                    }
                } else {
                    response = "failure";
                }
                signatureCallback.PostDocusignSendForESignature(envelopeData, isFinalize, returnUrl, docuSignUserAccountId, response);
            } catch(Exception ex) {
                log.LogError("Error creating envelope:", ex);
                throw;
            }
            return response;
        }

        /// <summary>
        /// Creates the DocuSign envelope from the pre-defined setup data and sends it for eSignature
        /// </summary>
        /// <param name="parentId"></param>
        /// <param name="docuSignUserAccountId"></param>
        /// <returns>string</returns>
        public async Task<string> CreateEnvelopeFromSetupData(string parentId, string docuSignUserAccountId) {
            string response = string.Empty;
            try {

                // If no account id is passed explicitly to the API, check if there is a pre-defined account saved in the CRM
                if(string.IsNullOrWhiteSpace(docuSignUserAccountId)) {
                    DocuSignPredefinedEnvelopeContentDTO predefinedSetup = new DocuSignPredefinedEnvelopeContentDTO();
                    predefinedSetup = await notificationSetupRepository.Get(parentId);
                    docuSignUserAccountId = predefinedSetup.SelectedDocuSignUserId;
                }

                var dep = new AppInsightsDependency("EnvelopesApi::InitializeClientUsingToken", "DocuSignRESTServiceManager::CreateEnvelopeFromSetupData", apiContext.RequestId);
                await InitializeClientUsingToken(parentId, string.Empty, docuSignUserAccountId);
                dep.End();

                var envDef = await PrepareEnvelopeContentUsingSetupData(parentId);

                dep = new AppInsightsDependency("EnvelopesApi::CreateEnvelope", "DocuSignRESTServiceManager::CreateEnvelopeFromSetupData", apiContext.RequestId);
                var envelopeSummary = docuSignEnvelopesApiClient.CreateEnvelope(docuSignUserData.DocuSignAccountId, envDef);
                dep.End();

                var envRecordId = await envelopeRepo.SaveEnvelopeData(parentId, envelopeSummary.EnvelopeId, envDef.ToJson(), envelopeSummary.Status, envelopeSummary.StatusDateTime, docuSignUserData.DocuSignUserCrmRecordId);
                if(envRecordId != null && !envRecordId.Equals(Guid.Empty)) {

                    dep = new AppInsightsDependency("EnvelopesApi::ListRecipients", "DocuSignRESTServiceManager::CreateEnvelopeFromSetupData", apiContext.RequestId);
                    var envRecipientsStatus = docuSignEnvelopesApiClient.ListRecipients(docuSignUserData.DocuSignAccountId, envelopeSummary.EnvelopeId);
                    dep.End();

                    await envelopeRecipientStatusRepo.SaveEnvelopeRecipientData(parentId, envRecordId, envelopeSummary.EnvelopeId, envRecipientsStatus.ToJson());
                    response = "success";
                } else {
                    response = "failure";
                }

            } catch(Exception ex) {
                log.LogError("Error creating envelope:", ex);
                throw;
            }
            return response;
        }

        /// <summary>
        /// Recall/Void DocuSign envelope
        /// </summary>
        /// <param name="parentId"></param>
        /// <returns>bool</returns>
        public async Task<bool> RecallEnvelope(string parentId, string voidedReason) {
            var isRecallSuccessful = true;
            try {
                var envelope = await envelopeRepo.GetEnvelopeByParentId(parentId);
                var envelopeRecordId = envelope.GetId();
                var envelopeId = envelope.GetName();
                var currentStatus = envelope.GetValue<string>(DOCUSIGNENVELOPE_FIELD_ENVELOPE_STATUS);

                var dep = new AppInsightsDependency("EnvelopesApi::InitializeClientUsingToken", "DocuSignRESTServiceManager::RecallEnvelope", apiContext.RequestId);
                await InitializeClientUsingToken(parentId, envelopeRecordId.ToString(), string.Empty);
                dep.End();

                var envToBeUpdated = new Envelope();
                envToBeUpdated.EnvelopeId = envelopeId;
                envToBeUpdated.Status = DocuSignConstants.ENVELOPE_STATUS_VOIDED;
                if(string.IsNullOrEmpty(voidedReason)) {
                    envToBeUpdated.VoidedReason = "Sender" + DEFAULT_VOIDED_REASON;
                } else {
                    envToBeUpdated.VoidedReason = voidedReason;
                }

                dep = new AppInsightsDependency("EnvelopesApi::Update", "DocuSignRESTServiceManager::RecallEnvelope", apiContext.RequestId);
                var envUpdateStatus = docuSignEnvelopesApiClient.Update(docuSignUserData.DocuSignAccountId, envelopeId, envToBeUpdated);
                dep.End();

                if(envUpdateStatus.ErrorDetails == null) {
                    isRecallSuccessful = true;

                    dep = new AppInsightsDependency("EnvelopesApi::GetEnvelope", "DocuSignRESTServiceManager::RecallEnvelope", apiContext.RequestId);
                    var envStatus = docuSignEnvelopesApiClient.GetEnvelope(docuSignUserData.DocuSignAccountId, envelopeId);
                    dep.End();

                    var envRecordId = await envelopeRepo.UpdateEnvelopeStatus(envelopeId, envStatus.Status, envStatus.StatusChangedDateTime, currentStatus, envelopeRecordId);
                    if(envRecordId.Equals(Guid.Empty)) {
                        isRecallSuccessful = false;
                    }
                } else {
                    isRecallSuccessful = false;
                }
            } catch(Exception ex) {
                log.LogError("Error recalling envelope:", ex);
                throw;
            }

            return isRecallSuccessful;
        }

        /// <summary>
        /// Correct DocuSign envelope
        /// </summary>
        /// <param name="parentId"></param>
        /// <returns>string</returns>
        public async Task<string> CorrectEnvelope(string parentId, string returnUrl) {
            var response = string.Empty;
            try {
                var envelope = await envelopeRepo.GetEnvelopeByParentId(parentId);
                var envelopeId = envelope.GetName();

                var dep = new AppInsightsDependency("EnvelopesApi::InitializeClientUsingToken", "DocuSignRESTServiceManager::CorrectEnvelope", apiContext.RequestId);
                await InitializeClientUsingToken(parentId, envelope.GetId(), string.Empty);
                dep.End();

                CorrectViewRequest correctRequest = new CorrectViewRequest();
                correctRequest.ReturnUrl = returnUrl;

                dep = new AppInsightsDependency("EnvelopesApi::CreateCorrectView", "DocuSignRESTServiceManager::CorrectEnvelope", apiContext.RequestId);
                var correctViewUrl = docuSignEnvelopesApiClient.CreateCorrectView(docuSignUserData.DocuSignAccountId, envelopeId, correctRequest);
                dep.End();

                response = correctViewUrl.Url;

            } catch(Exception ex) {
                log.LogError("Error connecting to the correct envelope console:", ex);
                throw;
            }

            return response;
        }

        private async Task AddSignedDocument(string envelopeId, string envelopeParentId, List<string> relatedAgreementIds = null, string requestSource = null) {
            var shouldCombineDocuments = await ShouldCombineSignedEnvelopeDocuments(envelopeParentId);
            var addSignedDocumentTask = shouldCombineDocuments ? AddCombinedSignedDocument(envelopeId, envelopeParentId, relatedAgreementIds, requestSource) : AddSeparateSignedDocuments(envelopeId, envelopeParentId, requestSource);
            await addSignedDocumentTask;
        }

        private async Task AddCombinedSignedDocument(string envelopeId, string envelopeParentId, List<string> relatedAgreementIds = null, string requestSource = null) {
            var fileName = await GetCombinedSignedDocumentFileName(envelopeParentId);
            var combinedSignedDocumentContent = await GetEnvelopeDocumentContent(envelopeId, DocuSignConstants.DOCUMENT_ID_COMBINED, requestSource);

            await ProcessSignedDocumentCreation(envelopeParentId, envelopeId, fileName, documentContent: combinedSignedDocumentContent, requestSource: requestSource);

            if(relatedAgreementIds?.Count > 0) {
                foreach(var relatedAgreementId in relatedAgreementIds) {
                    var shouldAddSignedDocumentToRelatedAgreement = await ShouldAddSignedDocumentToRelatedAgreement(relatedAgreementId);
                    if(shouldAddSignedDocumentToRelatedAgreement) {
                        await ProcessSignedDocumentCreation(relatedAgreementId, envelopeId, fileName, documentContent: combinedSignedDocumentContent, requestSource: requestSource);
                    }
                }
            }
        }

        private async Task<string> GetCombinedSignedDocumentFileName(string envelopeParentId) {
            var shouldRetainOriginalFileNameAfterSignature = await ShouldRetainOriginalFileNameAfterSignature();
            var fileName = shouldRetainOriginalFileNameAfterSignature ? GetFileNameWithAttachmentName(envelopeParentId) : GetFileNameWithAgreementName(envelopeParentId);
            return $"{await fileName}{DocuSignConstants.PDF_SUFFIX}";
        }

        private async Task<bool> ShouldRetainOriginalFileNameAfterSignature() {
            var retainOriginalFileNameAfterSignatureProductSetting = await productSettingHelper.GetAsync<string>(DocuSignConstants.DOCUSIGN_RETAIN_FILE_NAME_SETTING);
            bool retainOriginalFileNameAfterSignatureProductSettingValue;
            bool.TryParse(retainOriginalFileNameAfterSignatureProductSetting, out retainOriginalFileNameAfterSignatureProductSettingValue);
            return retainOriginalFileNameAfterSignatureProductSettingValue;
        }

        private async Task<string> GetFileNameWithAttachmentName(string envelopeParentId) {
            var attachmentIds = await attachmentSetupRepository.Get(envelopeParentId);
            if(!(attachmentIds?.Count() == 1)) {
                return await GetFileNameWithAgreementName(envelopeParentId);
            }

            var attachments = await agreementDocRepo.GetDocumentsById(attachmentIds.ToList(), null);
            if(!(attachments?.Count() > 0)) {
                throw new Exception("The attachment selected while sending for eSignature doesn't exist anymore.");
            }
            return Utilities.GetFileNameWithoutExtension(attachments.First().GetName());
        }

        private async Task<string> GetFileNameWithAgreementName(string agreementId) {
            var agreementName = await GetDocumentParentName(agreementId);
            return $"{agreementName}{DocuSignConstants.SIGNED_SUFFIX}";
        }

        private async Task ProcessSignedDocumentCreation(string agreementId, string envelopeId, string fileName, string documentId = null, byte[] documentContent = null, string requestSource = null) {
            try {
                var isVersionAware = true;

                documentContent = documentContent ?? await GetEnvelopeDocumentContent(envelopeId, documentId, requestSource);
                var createdDocumentId = await agreementDocRepo.CreateDocument(agreementId, isVersionAware, fileName, Common.GlobalConstants.MIME_TYPE_PDF, documentContent, null, GetSelectOptionObject(Convert.ToString((int)DocumentType.EXECUTEDDOCUMENT)));
                await documentVersionRepo.MarkDocumentAsExecuted(new string[] { createdDocumentId });
            } catch(Exception ex) {
                log.LogError("Error adding the signed doc:", ex);
                throw;
            }
        }

        private async Task<byte[]> GetEnvelopeDocumentContent(string envelopeId, string documentId, string requestSource = null) {
            var dep = new AppInsightsDependency("EnvelopesApi::GetDocumentAsync", $"DocuSignRESTServiceManager::{requestSource}", apiContext.RequestId);
            using(var documentContentStream = await docuSignEnvelopesApiClient.GetDocumentAsync(docuSignUserData.DocuSignAccountId, envelopeId, documentId)) {
                dep.End();
                return await Utilities.GetBytes(documentContentStream);
            }
        }

        private SelectOption GetSelectOptionObject(string optionKey, string optionValue = null) {
            return new SelectOption {
                Key = optionKey,
                Value = optionValue
            };
        }

        private async Task AddSeparateSignedDocuments(string envelopeId, string envelopeParentId, string requestSource = null) {
            var documents = await GetEnvelopeDocuments(envelopeId, envelopeParentId, requestSource);
            if(documents != null) {
                var documentParentGroups = documents.GroupBy(document => {
                    var documentParentId = GetEnvelopeDocumentFieldValue(document.DocumentFields, DOCUSIGNENVELOPE_FIELD_PARENT_ID);
                    return IsSignerAttachment(documentParentId) ? envelopeParentId : documentParentId;
                });
                if(documentParentGroups.Any()) {
                    var shouldRetainOriginalFileNameAfterSignature = await ShouldRetainOriginalFileNameAfterSignature();
                    foreach(var documentParentGroup in documentParentGroups) {
                        var documentNumber = documentParentGroup.Count() > 1 ? 1 : 0; //This is to ensure that the document number should be appended only in case of multiple documents
                        var documentParentName = await GetDocumentParentName(documentParentGroup.Key);

                        foreach(var document in documentParentGroup) {
                            var fileName = GetSeparateSignedDocumentFileName(documentParentName, document.Name, shouldRetainOriginalFileNameAfterSignature, documentNumber++);
                            await ProcessSignedDocumentCreation(documentParentGroup.Key, envelopeId, fileName, document.DocumentId, requestSource: requestSource);
                        }
                    }
                }
            }
        }

        private async Task<IEnumerable<EnvelopeDocument>> GetEnvelopeDocuments(string envelopeId, string envelopeParentId, string requestSource = null) {
            var dep = new AppInsightsDependency("EnvelopesApi::ListDocumentsAsync", $"DocuSignRESTServiceManager::{requestSource}", apiContext.RequestId);
            var documentResponse = await docuSignEnvelopesApiClient.ListDocumentsAsync(docuSignUserData.DocuSignAccountId, envelopeId);
            dep.End();

            if(documentResponse?.EnvelopeDocuments?.Count > 0) {
                var contentDocuments = documentResponse.EnvelopeDocuments.Where(document => document.Type.Equals(DocuSignConstants.DOCUMENT_TYPE_CONTENT));
                if(contentDocuments.Any()) {
                    var documentFieldTasks = contentDocuments.Select(async contentDocument => {
                        contentDocument.DocumentFields = await GetEnvelopeDocumentFields(envelopeId, contentDocument.DocumentId, requestSource);
                    });
                    await Task.WhenAll(documentFieldTasks);
                    return contentDocuments;
                }
            }
            return null;
        }

        private async Task<List<NameValue>> GetEnvelopeDocumentFields(string envelopeId, string documentId, string requestSource = null) {
            var dep = new AppInsightsDependency("EnvelopesApi::ListDocumentFieldsAsync", $"DocuSignRESTServiceManager::{requestSource}", apiContext.RequestId);
            var documentFieldsResponse = await docuSignEnvelopesApiClient.ListDocumentFieldsAsync(docuSignUserData.DocuSignAccountId, envelopeId, documentId);
            dep.End();

            return documentFieldsResponse?.DocumentFields;
        }

        private string GetSeparateSignedDocumentFileName(string agreementName, string documentName, bool shouldRetainOriginalFileNameAfterSignature, int documentNumber = 0) {
            var fileName = shouldRetainOriginalFileNameAfterSignature ? Utilities.GetFileNameWithoutExtension(documentName) : $"{agreementName}{DocuSignConstants.SIGNED_SUFFIX}{GetFileNameDocumentNumberSuffix(documentNumber)}";
            return $"{fileName}{DocuSignConstants.PDF_SUFFIX}";
        }

        private string GetFileNameDocumentNumberSuffix(int documentNumber) {
            return documentNumber > 0 ? $"_{documentNumber}" : string.Empty;
        }

        private string GetEnvelopeDocumentFieldValue(List<NameValue> documentFields, string documentFieldName) {
            return documentFields?.FirstOrDefault(documentField => documentField.Name.Equals(documentFieldName, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        /// <summary>
        /// Checks if the document is uploaded by the signer directly during the signing based on the given parent id
        /// </summary>
        /// <param name="documentParentId">The parent id of the document</param>
        /// <returns>True if the parent id is null else False</returns>
        /// <remarks>This validation assumes that all envelope documents added during the envelope creation would have the parent Id. This method is created to incorporate any evolutionary changes to this validation logic</remarks>
        private bool IsSignerAttachment(string documentParentId) {
            return documentParentId == null;
        }

        private async Task<string> GetDocumentParentName(string documentParentId) {
            var agreement = await agreementRepo.GetAgreement(documentParentId, new List<string> { DATA_OBJECT_FIELD_NAME });
            return agreement?.GetName();
        }

        private async Task ProcessRelatedAgreements(string envelopeParentId, string envelopeStatus, List<string> relatedAgreementIds) {
            await relatedAgreementHelper.UpdateRelatedAgreementStatus(SignatureUtil.GetSignatureStatusForDocusign(envelopeStatus), relatedAgreementIds, envelopeParentId);
        }

        private List<RelatedAgreementJson> GetEnvelopeRelatedAgreements(IObjectInstance envelopeRecord) {
            var relatedAgreementJson = envelopeRecord.GetValue<string>(DOCUSIGNENVELOPE_FIELD_RELATEDAGREEMENTJSON);
            return !string.IsNullOrEmpty(relatedAgreementJson) ? JsonConvert.DeserializeObject<List<RelatedAgreementJson>>(relatedAgreementJson) : null;
        }

        private async Task<bool> ShouldAddSignedDocumentToRelatedAgreement(string relatedAgreementId) {
            var relatedAgreement = await agreementRepo.GetAgreement(relatedAgreementId, new List<string> { AGREEMENT_FIELD_STATUS });
            var relatedAgreementStatus = relatedAgreement?.GetValue<SelectOption>(AGREEMENT_FIELD_STATUS)?.Key;
            return relatedAgreementStatus == AgreementLifecycleHelper.STATUS_PENDING_RELATED_AGREEMENT;
        }

        /// <summary>
        /// Prepare envelope definition for sending to DocuSign
        /// </summary>
        /// <param name="envelopeData"></param>
        /// <param name="isFinalize"></param>
        /// <returns>EnvelopeDefinition of the envelope to be sent to DocuSign</returns>
        public async Task<EnvelopeDefinition> PrepareEnvelopeContent(DocuSignEnvelopeDTO envelopeData, bool isFinalize) {
            try {
                // Add the email subject & body
                if(!string.IsNullOrWhiteSpace(envelopeData.emailsubject)) {
                    envDef.EmailSubject = envelopeData.emailsubject;
                }

                if(!string.IsNullOrWhiteSpace(envelopeData.emailbody)) {
                    envDef.EmailBlurb = envelopeData.emailbody;
                }

                await AddEventNotifications();
                AddNotification(envelopeData);
                await AddDocuments(envelopeData);
                AddRecipients(envelopeData);

                if(!isFinalize) {
                    // set envelope status as sent for sending it
                    envDef.Status = DocuSignConstants.ENVELOPE_STATUS_SENT;
                } else {
                    // set envelope status as created for finalize
                    envDef.Status = DocuSignConstants.ENVELOPE_STATUS_CREATED;
                }
            } catch(Exception ex) {
                log.LogError("Error preparing the envelope content:", ex);
                throw;
            }
            return envDef;
        }

        /// <summary>
        /// Prepare envelope definition from pre-defined setup data for sending to DocuSign
        /// </summary>
        /// <param name="parentId"></param>
        /// <returns>EnvelopeDefinition of the envelope to be sent to DocuSign</returns>
        private async Task<EnvelopeDefinition> PrepareEnvelopeContentUsingSetupData(string parentId) {
            try {

                // Add the email subject & body
                var emailContent = await GetEmailContent(parentId);

                if(emailContent != null) {
                    envDef.EmailSubject = string.IsNullOrWhiteSpace(emailContent.GetValue<string>("subject")) ? DocuSignConstants.DOCUSIGN_ENVELOPE_DEFAULT_SUBJECT : StripHTML(emailContent.GetValue<string>("subject"));
                    envDef.EmailBlurb = string.IsNullOrWhiteSpace(emailContent.GetValue<string>("body")) ? string.Empty : StripHTML(emailContent.GetValue<string>("body"));
                } else {
                    envDef.EmailSubject = DocuSignConstants.DOCUSIGN_ENVELOPE_DEFAULT_SUBJECT;
                }

                await AddEventNotifications();
                await AddPredefinedNotification(parentId);
                await AddPredefinedDocuments(parentId);
                await AddPredefinedRecipients(parentId);

                envDef.Status = DocuSignConstants.ENVELOPE_STATUS_SENT;

            } catch(Exception ex) {
                log.LogError("Error preparing the envelope content with setup data:", ex);
                throw;
            }
            return envDef;
        }

        // Add notification to envelope definition: reminders & expiration
        private void AddNotification(DocuSignEnvelopeDTO envelopeData) {
            try {
                if(envelopeData.notification != null) {
                    envDef.Notification = new DocuSign.eSign.Model.Notification();
                    envDef.Notification.Reminders = new Reminders();
                    envDef.Notification.Expirations = new Expirations();

                    if(UseNotificationDefaults(envelopeData.notification, null)) {
                        envDef.Notification.UseAccountDefaults = "true";
                    } else {

                        if(!string.IsNullOrWhiteSpace(envelopeData.notification.ReminderDays)) {
                            envDef.Notification.Reminders.ReminderDelay = envelopeData.notification.ReminderDays;
                            useAccountDefaults = "false";
                            reminderEnabled = "true";
                        }

                        if(!string.IsNullOrWhiteSpace(envelopeData.notification.ReminderFrequency)) {
                            envDef.Notification.Reminders.ReminderFrequency = envelopeData.notification.ReminderFrequency;
                            useAccountDefaults = "false";
                            reminderEnabled = "true";
                        }

                        if(!string.IsNullOrWhiteSpace(envelopeData.notification.ExpireDays)) {
                            envDef.Notification.Expirations.ExpireAfter = envelopeData.notification.ExpireDays;
                            useAccountDefaults = "false";
                            expireEnabled = "true";
                        }

                        if(!string.IsNullOrWhiteSpace(envelopeData.notification.ExpireWarn)) {
                            envDef.Notification.Expirations.ExpireWarn = envelopeData.notification.ExpireWarn;
                            useAccountDefaults = "false";
                            expireEnabled = "true";
                        }

                        envDef.Notification.Reminders.ReminderEnabled = reminderEnabled;
                        envDef.Notification.Expirations.ExpireEnabled = expireEnabled;
                        envDef.Notification.UseAccountDefaults = useAccountDefaults;
                    }
                }
            } catch(Exception ex) {
                log.LogError("Error adding notifcation params to the envelope:", ex);
                throw;
            }
        }

        private async Task AddPredefinedNotification(string parentId) {
            try {
                var notificationParams = await notificationSetupRepository.Get(parentId);
                envDef.Notification = new DocuSign.eSign.Model.Notification();
                if(notificationParams != null) {
                    if(UseNotificationDefaults(null, notificationParams.notification)) {
                        envDef.Notification.UseAccountDefaults = "true";
                    } else {
                        envDef.Notification.Reminders = new Reminders();
                        envDef.Notification.Expirations = new Expirations();
                        envDef.Notification.Reminders.ReminderDelay = notificationParams.notification.ReminderDays;
                        envDef.Notification.Reminders.ReminderFrequency = notificationParams.notification.ReminderFrequency;
                        envDef.Notification.Expirations.ExpireAfter = notificationParams.notification.ExpireDays;
                        envDef.Notification.Expirations.ExpireWarn = notificationParams.notification.ExpireWarn;
                    }
                } else {
                    envDef.Notification.UseAccountDefaults = "true";
                }

            } catch(Exception ex) {
                log.LogError("Error adding predefined notification params:", ex);
                throw;
            }
        }

        private bool UseNotificationDefaults(DocuSignEnvelopeNotificationDTO notification, DocuSignEnvelopeNotificationDTO predefinedNotification) {
            if(notification != null) {
                return ((!string.IsNullOrWhiteSpace(notification.ReminderDays)) &&
                        (!string.IsNullOrWhiteSpace(notification.ReminderFrequency)) &&
                        (!string.IsNullOrWhiteSpace(notification.ExpireDays)) &&
                        (!string.IsNullOrWhiteSpace(notification.ExpireWarn)) &&
                        notification.ReminderDays.Equals("0") &&
                        notification.ReminderFrequency.Equals("0") &&
                        notification.ExpireDays.Equals("0") &&
                        notification.ExpireWarn.Equals("0"));
            } else {
                return ((!string.IsNullOrWhiteSpace(predefinedNotification.ReminderDays)) &&
                        (!string.IsNullOrWhiteSpace(predefinedNotification.ReminderFrequency)) &&
                        (!string.IsNullOrWhiteSpace(predefinedNotification.ExpireDays)) &&
                        (!string.IsNullOrWhiteSpace(predefinedNotification.ExpireWarn)) &&
                        predefinedNotification.ReminderDays.Equals("0") &&
                        predefinedNotification.ReminderFrequency.Equals("0") &&
                        predefinedNotification.ExpireDays.Equals("0") &&
                        predefinedNotification.ExpireWarn.Equals("0"));
            }
        }

        /// <summary>
        /// Add documents to envelope definition
        /// </summary>
        /// <param name="envelopeData"></param>
        private async Task AddDocuments(DocuSignEnvelopeDTO envelopeData) {
            try {
                var index = 1;
                envDef.Documents = new List<Document>();

                foreach(var attachment in envelopeData.attachments) {
                    var attachmentContent = await agreementDocumentManager.GetDocumentContent(attachment.AttachmentId);

                    envDef.Documents.Add(new Document() {
                        ApplyAnchorTabs = "true",
                        DocumentBase64 = Convert.ToBase64String(attachmentContent.DocFileStream),
                        DocumentFields = new List<NameValue> {
                            GetEnvelopeDocumentFieldObject(DOCUSIGNENVELOPE_FIELD_PARENT_ID, attachment.ParentId)
                        },
                        DocumentId = index.ToString(),
                        FileExtension = Path.GetExtension(attachmentContent.FileName),
                        Name = Utilities.GetFileNameWithoutExtension(attachmentContent.FileName),
                        TransformPdfFields = "true"
                    });

                    index = index + 1;
                }
            } catch(Exception ex) {
                log.LogError("Error adding documents to the envelope content:", ex);
                throw;
            }
        }

        private NameValue GetEnvelopeDocumentFieldObject(string fieldName, string fieldValue) {
            return new NameValue {
                Name = fieldName,
                Value = fieldValue
            };
        }

        /// <summary>
        /// Add documents from the pre-defined setup data to envelope definition
        /// </summary>
        /// <param name="parentId"></param>
        private async Task AddPredefinedDocuments(string parentId) {
            try {
                envDef.Documents = new List<Document>();
                int index = 1;
                var selectedAttachmentIds = await attachmentSetupRepository.Get(parentId);

                if(selectedAttachmentIds != null && !selectedAttachmentIds.Count<string>().Equals(0)) {
                    foreach(var attachId in selectedAttachmentIds) {
                        var doc = new Document();
                        var attachment = await agreementDocumentManager.GetDocumentContent(attachId);
                        var docBody = attachment.DocFileStream;
                        doc.ApplyAnchorTabs = "true";
                        doc.TransformPdfFields = "true";
                        doc.DocumentBase64 = Convert.ToBase64String(docBody);
                        doc.Name = attachment.FileName;

                        doc.FileExtension = Path.GetExtension(doc.Name);
                        doc.DocumentId = index.ToString();
                        envDef.Documents.Add(doc);
                        index = index + 1;
                    }
                }
            } catch(Exception ex) {
                log.LogError("Error adding predefined documents:", ex);
                throw;
            }
        }

        /// <summary>
        /// Add recipients to envelope definition
        /// </summary>
        /// <param name="envelopeData"></param>
        private void AddRecipients(DocuSignEnvelopeDTO envelopeData) {
            try {
                envDef.Recipients = new Recipients();
                envDef.Recipients.Signers = new List<Signer>();
                envDef.Recipients.CarbonCopies = new List<CarbonCopy>();
                envDef.Recipients.InPersonSigners = new List<InPersonSigner>();
                bool isSigningGroup;

                int index = 1;
                foreach(var recipient in envelopeData.recipients) {
                    isSigningGroup = recipient.RecipientCategory.Equals(DocuSignConstants.RECIPIENT_CATEGORY_SIGNING_GROUP, StringComparison.InvariantCultureIgnoreCase);
                    if(recipient.RecipientType.Equals(DocuSignConstants.RECIPIENT_TYPE_SIGNER, StringComparison.InvariantCultureIgnoreCase)) {
                        Signer signer = new Signer();
                        if(isSigningGroup) {
                            signer.SigningGroupId = recipient.SigningGroupId;
                            signer.SigningGroupName = recipient.SigningGroupName;
                        } else {
                            signer.Name = recipient.RecipientFullName;
                            signer.Email = recipient.Email;
                        }
                        signer.RoutingOrder = recipient.RoutingOrder;
                        signer.RecipientId = index.ToString();
                        signer.CustomFields = new List<string>();
                        signer.CustomFields.Add(recipient.RecipientCategory);
                        signer.CustomFields.Add(recipient.IsInternal.ToString());
                        if(isSigningGroup && !string.IsNullOrEmpty(recipient.SigningGroupEmail)) {
                            signer.CustomFields.Add(recipient.SigningGroupEmail.ToString());
                        }
                        signer.Tabs = null;
                        envDef.Recipients.Signers.Add(signer);
                    } else if(recipient.RecipientType.Equals(DocuSignConstants.RECIPIENT_TYPE_CARBON_COPY, StringComparison.InvariantCultureIgnoreCase)) {
                        var carbonCopy = new CarbonCopy();
                        if(isSigningGroup) {
                            carbonCopy.SigningGroupId = recipient.SigningGroupId;
                            carbonCopy.SigningGroupName = recipient.SigningGroupName;
                        } else {
                            carbonCopy.Name = recipient.RecipientFullName;
                            carbonCopy.Email = recipient.Email;
                        }
                        carbonCopy.RoutingOrder = recipient.RoutingOrder;
                        carbonCopy.RecipientId = index.ToString();
                        carbonCopy.CustomFields = new List<string>();
                        carbonCopy.CustomFields.Add(recipient.RecipientCategory);
                        carbonCopy.CustomFields.Add(recipient.IsInternal.ToString());
                        if(isSigningGroup && !string.IsNullOrEmpty(recipient.SigningGroupEmail)) {
                            carbonCopy.CustomFields.Add(recipient.SigningGroupEmail.ToString());
                        }
                        envDef.Recipients.CarbonCopies.Add(carbonCopy);
                    } else if(recipient.RecipientType.Equals(DocuSignConstants.RECIPIENT_TYPE_IN_PERSON_SIGNER, StringComparison.InvariantCultureIgnoreCase)) {
                        var inPersonSigner = new InPersonSigner();
                        if(isSigningGroup) {
                            inPersonSigner.SigningGroupId = recipient.SigningGroupId;
                            inPersonSigner.SigningGroupName = recipient.SigningGroupName;
                        } else {
                            inPersonSigner.Name = recipient.RecipientFullName;
                            inPersonSigner.Email = recipient.Email;
                        }
                        inPersonSigner.HostEmail = docuSignUserData.DocuSignUserEmail;
                        inPersonSigner.HostName = docuSignUserData.DocuSignUserName;
                        inPersonSigner.RoutingOrder = recipient.RoutingOrder;
                        inPersonSigner.CustomFields = new List<string>();
                        inPersonSigner.CustomFields.Add(recipient.RecipientCategory);
                        inPersonSigner.CustomFields.Add(recipient.IsInternal.ToString());
                        inPersonSigner.RecipientId = index.ToString();
                        inPersonSigner.Tabs = null;
                        if(isSigningGroup && !string.IsNullOrEmpty(recipient.SigningGroupEmail)) {
                            inPersonSigner.CustomFields.Add(recipient.SigningGroupEmail.ToString());
                        }
                        envDef.Recipients.InPersonSigners.Add(inPersonSigner);
                    }

                    index = index + 1;
                }
            } catch(Exception ex) {
                log.LogError("Error adding recipients to envelope content:", ex);
                throw;
            }
        }

        /// <summary>
        /// Add recipients to envelope definition
        /// </summary>
        /// <param name="parentId"></param>
        private async Task AddPredefinedRecipients(string parentId) {
            try {
                envDef.Recipients = new Recipients();
                envDef.Recipients.Signers = new List<Signer>();
                envDef.Recipients.CarbonCopies = new List<CarbonCopy>();
                envDef.Recipients.InPersonSigners = new List<InPersonSigner>();

                var recipients = new List<DocuSignEnvelopeDTO.Recipient>();

                var recipientSetupData = await docuSignDefaultRecipientRepository.GetRecipientData(Guid.Parse(parentId));
                var recipientSetupRecords = new List<Dictionary<string, object>>();
                recipientSetupData.ForEach(x => recipientSetupRecords.Add(x.Serialize()));
                recipientSetupRecords.ForEach(x => recipients.Add(TransformAndAssignRecipientProperties(x)));
                int index = 1;
                foreach(var recipient in recipients) {
                    recipient.RecipientFullName = recipient.FirstName + " " + recipient.LastName;
                    if(recipient.RecipientType.Equals(DocuSignConstants.RECIPIENT_TYPE_SIGNER, StringComparison.InvariantCultureIgnoreCase)) {
                        Signer signer = new Signer();
                        signer.Name = recipient.RecipientFullName;
                        signer.Email = recipient.Email;
                        signer.RoutingOrder = recipient.RoutingOrder;
                        signer.RecipientId = index.ToString();
                        signer.CustomFields = new List<string>();
                        signer.CustomFields.Add(recipient.RecipientCategory);
                        signer.CustomFields.Add(recipient.IsInternal.ToString());
                        signer.Tabs = null;
                        envDef.Recipients.Signers.Add(signer);
                    } else if(recipient.RecipientType.Equals(DocuSignConstants.RECIPIENT_TYPE_CARBON_COPY, StringComparison.InvariantCultureIgnoreCase)) {
                        var carbonCopy = new CarbonCopy();
                        carbonCopy.Name = recipient.RecipientFullName;
                        carbonCopy.Email = recipient.Email;
                        carbonCopy.RoutingOrder = recipient.RoutingOrder;
                        carbonCopy.RecipientId = index.ToString();
                        carbonCopy.CustomFields = new List<string>();
                        carbonCopy.CustomFields.Add(recipient.RecipientCategory);
                        carbonCopy.CustomFields.Add(recipient.IsInternal.ToString());
        private async Task RemoveSigningGroupData(List<IObjectInstance> recipientSetupData, IEnumerable<IObjectInstance> tobeDeleted) {
            await docuSignDefaultRecipientRepository.DeleteRecipients(tobeDeleted.Select(x => x.GetId()).ToList());
            tobeDeleted.ToList().ForEach(x => recipientSetupData.Remove(x));
        }

        /// <summary>
        /// Gets Docusign signing groups
        /// </summary>
        /// <param name="docuSignUserId">DocusignUser Id</param>        
        /// <returns></returns>
        public async Task<DocuSignSigningGroupsDTO> GetSigningGroups(string docuSignUserId) {
            SigningGroupInformation signingGroup;
            try {
                signingGroup = await GetSigningGroupFromDocusign(docuSignUserId, true);
            } catch {
                throw new ApplicationException("Error in retrieving signing groups for this account");
            }
            var signigGroupDTO = ValidateAndGetSigningDTO(signingGroup);
            return signigGroupDTO;
        }

        private async Task<SigningGroupInformation> GetSigningGroupFromDocusign(string docuSignUserId, bool isIncludeUsers) {
            var docuSignUserData = await docuSignUserRepo.Get(null, null, docuSignUserId);
            var signingGroupsApiClient = GetSigningGroupsAPIClient(docuSignUserId, docuSignUserData);
            SigningGroupsApi.ListOptions options = new SigningGroupsApi.ListOptions {
                includeUsers = Convert.ToString(isIncludeUsers)
            };
            return await signingGroupsApiClient.ListAsync(docuSignUserData.DocuSignAccountId, options);
        }

        private DocuSignSigningGroupsDTO ValidateAndGetSigningDTO(SigningGroupInformation signingGroup) {
            DocuSignSigningGroupsDTO signigGroupDTO = null;
            if(signingGroup != null && signingGroup.Groups != null && signingGroup.Groups.Count > 0) {
                signigGroupDTO = JsonConvert.DeserializeObject<DocuSignSigningGroupsDTO>(signingGroup.ToJson());
            } else {
                throw new ApplicationException("No Signing Group exists for this account");
            }
            return signigGroupDTO;
        }

        private SigningGroupsApi GetSigningGroupsAPIClient(string docuSignUserAccountId, DocuSignUserDTO docuSignUserData) {
            var oAuthHeader = "bearer " + EncryptionUtil.DecryptStringAES(docuSignUserData.AccessTokenData.AccessToken, DocuSignConstants.KEY_PHRASE, DocuSignConstants.IV_PHRASE);

            // initialize client for desired environment
            var apiClient = new ApiClient(docuSignUserData.ServerInstanceUrl);
            Configuration cfg = new Configuration(apiClient);
            cfg.DefaultHeader.Clear();
            cfg.DefaultHeader.Remove(DocuSignConstants.OAUTH_HEADER);
            cfg.DefaultHeader.Add(DocuSignConstants.OAUTH_HEADER, oAuthHeader);

            return new SigningGroupsApi(cfg);
        }

        /// <summary>
        /// Parses the HTML email content extracted from the default email template
        /// https://www.codeproject.com/Articles/11902/Convert-HTML-to-Plain-Text
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        private string StripHTML(string source) {

            string result = string.Empty;
            try {

                // Remove HTML Development formatting
                // Replace line breaks with space
                // because browsers inserts space
                result = source.Replace("\r", " ");
                // Replace line breaks with space
                // because browsers inserts space
                result = result.Replace("\n", " ");
                // Remove step-formatting
                result = result.Replace("\t", string.Empty);
                // Remove repeating spaces because browsers ignore them
                result = System.Text.RegularExpressions.Regex.Replace(result,
                                                                      @"( )+", " ");

                // Remove the header (prepare first by clearing attributes)
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"<( )*head([^>])*>", "<head>",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"(<( )*(/)( )*head( )*>)", "</head>",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         "(<head>).*(</head>)", string.Empty,
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // remove all scripts (prepare first by clearing attributes)
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"<( )*script([^>])*>", "<script>",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"(<( )*(/)( )*script( )*>)", "</script>",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                //result = System.Text.RegularExpressions.Regex.Replace(result,
                //         @"(<script>)([^(<script>\.</script>)])*(</script>)",
                //         string.Empty,
                //         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"(<script>).*(</script>)", string.Empty,
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // remove all styles (prepare first by clearing attributes)
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"<( )*style([^>])*>", "<style>",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"(<( )*(/)( )*style( )*>)", "</style>",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         "(<style>).*(</style>)", string.Empty,
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // insert tabs in spaces of <td> tags
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"<( )*td([^>])*>", "\t",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // insert line breaks in places of <BR> and <LI> tags
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"<( )*br( )*>", "\r",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"<( )*li( )*>", "\r",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // insert line paragraphs (double line breaks) in place
                // if <P>, <DIV> and <TR> tags
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"<( )*div([^>])*>", "\r\r",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"<( )*tr([^>])*>", "\r\r",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"<( )*p([^>])*>", "\r\r",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Remove remaining tags like <a>, links, images,
                // comments etc - anything that's enclosed inside < >
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"<[^>]*>", string.Empty,
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // replace special characters:
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @" ", " ",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"&bull;", " * ",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"&lsaquo;", "<",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"&rsaquo;", ">",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"&trade;", "(tm)",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"&frasl;", "/",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"&lt;", "<",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"&gt;", ">",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"&copy;", "(c)",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"&reg;", "(r)",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // Remove all others. More can be added, see
                // http://hotwired.lycos.com/webmonkey/reference/special_characters/
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         @"&(.{2,6});", string.Empty,
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // make line breaking consistent
                result = result.Replace("\n", "\r");

                // Remove extra line breaks and tabs:
                // replace over 2 breaks with 2 and over 4 tabs with 4.
                // Prepare first to remove any whitespaces in between
                // the escaped characters and remove redundant tabs in between line breaks
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         "(\r)( )+(\r)", "\r\r",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         "(\t)( )+(\t)", "\t\t",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         "(\t)( )+(\r)", "\t\r",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         "(\r)( )+(\t)", "\r\t",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // Remove redundant tabs
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         "(\r)(\t)+(\r)", "\r\r",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // Remove multiple tabs following a line break with just one tab
                result = System.Text.RegularExpressions.Regex.Replace(result,
                         "(\r)(\t)+", "\r\t",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // Initial replacement target string for line breaks
                string breaks = "\r\r\r";
                // Initial replacement target string for tabs
                string tabs = "\t\t\t\t\t";
                for(int index = 0; index < result.Length; index++) {
                    result = result.Replace(breaks, "\r\r");
                    result = result.Replace(tabs, "\t\t\t\t");
                    breaks = breaks + "\r";
                    tabs = tabs + "\t";
                }

            } catch(Exception ex) {
                log.LogError("Error converting the HTML to text", ex);
            }
            return result;
        }

    }
}
