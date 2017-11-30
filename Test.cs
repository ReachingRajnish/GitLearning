using Apttus.Contracts.Common;
using Apttus.Contracts.DAL.AzureSQL;
using Apttus.Contracts.DAL.Interfaces;
using Apttus.Contracts.Domain.Interfaces;
using Apttus.Contracts.Model.Enums;
using Apttus.Core.CommonObjects.Manager;
using Apttus.DataAccess.Common.CustomTypes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apttus.Contracts.Domain.Util {
    public class RelatedAgreementHelper : IRelatedAgreementHelper {
        internal IAgreementRepository agreementRepository { get; set; }
        internal IAgreementLifecycleHelper agmtLifecycleHelper { get; set; }
        internal IActivityManager activityManager { get; set; }

        private const string AGREEMENT_FIELD_NAME = "Name";
        private const string AGREEMENT_FIELD_STATUS = "status";
        private const string AGREEMENT_FIELD_STATUS_CATEGORY = "statuscategory";


        /// <summary>
        /// Updates Related Agreement Status
        /// </summary>
        /// <param name="signatureStatus">Signature Status to be Updated</param>
        /// <param name="relatedAgreementIds">List of Related Agreement ids</param>
        /// <param name="parentId">Parent Id</param>
        /// <returns></returns>
        public async Task UpdateRelatedAgreementStatus(SignatureStatus signatureStatus, List<string> relatedAgreementIds, string parentId) {
            ValidateParentId(parentId);
            var parentAgreement = await agreementRepository.GetAgreement(parentId, new List<string> { AGREEMENT_FIELD_NAME });
            var parentAgreementName = parentAgreement.GetValue<string>(AGREEMENT_FIELD_NAME);
            var fields = new List<string> { AGREEMENT_FIELD_STATUS, AGREEMENT_FIELD_STATUS_CATEGORY };

            foreach(var relatedAgreementId in relatedAgreementIds) {
                var relatedAgreement = await agreementRepository.GetAgreement(relatedAgreementId, fields);
                if(relatedAgreement != null) {
                    var status = relatedAgreement.GetValue<SelectOption>(AGREEMENT_FIELD_STATUS)?.Key;
                    var statusCategory = relatedAgreement.GetValue<SelectOption>(AGREEMENT_FIELD_STATUS_CATEGORY)?.Key;
                    var agreementLifecycleAction = string.Empty;

                    if(IsValidRelatedAgreementStatus(status, statusCategory)) {
                        switch(signatureStatus) {
                            case SignatureStatus.SENT:
                                agreementLifecycleAction = AgreementLifecycleAction.LIFECYCLE_ACTION_SENT_WITH_RELATED_AGREEMENT;
                                break;
                            case SignatureStatus.COMPLETED:
                                agreementLifecycleAction = AgreementLifecycleAction.LIFECYCLE_ACTION_FULLY_SIGNED;
                                break;
                            case SignatureStatus.DECLINED:
                                agreementLifecycleAction = AgreementLifecycleAction.LIFECYCLE_ACTION_SIGNATURE_DECLINED;
                                break;
                            case SignatureStatus.RECALLED:
                            case SignatureStatus.EXPIRED:
                                agreementLifecycleAction = AgreementLifecycleAction.LIFECYCLE_ACTION_SIGNATURE_REQUEST_RECALLED;
                                break;
                            default:
                                break;
                        }

                        var statusInfo = agmtLifecycleHelper.GetStatusInfoForAction(agreementLifecycleAction);
                        await agreementRepository.UpdateAgreementStatus(relatedAgreement, statusInfo.Item1, statusInfo.Item2);

                        await activityManager.Create(new Core.CommonObjects.Model.Activity {
                            ActivityDate = DateTime.UtcNow,
                            ContextObject = new Composite {
                                Id = relatedAgreementId,
                                Type = GlobalConstants.OBJECT_CLM_AGREEMENT
                            },
                            Description = $"Agreement eSignature request {signatureStatus.ToString().ToLowerInvariant()} with related agreement : {parentAgreementName}",
                            Name = GlobalConstants.CLM_ACTIVITY_NAME
                        });
                    }
                }
            }
        }

        private void ValidateParentId(string parentId) {
            Guid result;
            if(string.IsNullOrEmpty(parentId) || !Guid.TryParse(parentId, out result)) {
                throw new Exception("Invalid Parentid");
            }
        }

        private bool IsValidRelatedAgreementStatus(string status, string statusCategory) {
            return (statusCategory == AgreementLifecycleHelper.STATUS_CATEGORY_IN_SIGNATURES &&
                         (status == AgreementLifecycleHelper.STATUS_PENDING_RELATED_AGREEMENT ||
                          status == AgreementLifecycleHelper.STATUS_SIGNATURE_DECLINED ||
                          status == AgreementLifecycleHelper.STATUS_READY_FOR_SIGNATURE))
                  || (statusCategory == AgreementLifecycleHelper.STATUS_CATEGORY_IN_AUTHORING);
        }
    }
}
