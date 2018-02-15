
using Apttus.DocGen.Model.ExpressionEval;
using Apttus.LibraryDiagnostics;
using Apttus.Metadata.Client.Interface;
using Apttus.Metadata.Common.DTO.Runtime.V1;
using Apttus.Security.Common.Authentication.DTO.RequestContext;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apttus.DocGen.Domain.Implementations {

    public class TemplateManager : ITemplateManager {

        #region Constants
        private const string OBJECT_TEMPLATE = "dgn_Template";
        private const string OBJECT_TEMPLATE_RECORDTYPE = "dgn_Template_RecordType";
        private const string OBJECT_QUERYTEMPLATEQUALIFIER = "dgn_QueryTemplateQualifier";
        private const string OBJECT_SYSTEMUSER = "systemuser";
        private const string OBJECT_ORGANIZATION = "organization";

        private const string TEMPLATE_FIELD_BUSINESSOBJECT = "BusinessObject";
        private const string TEMPLATE_FIELD_PUBLISHEDDATE = "PublishedDate";
        private const string TEMPLATE_FIELD_NEEDSPUBLISHING = "NeedsPublishing";
        private const string TEMPLATE_FIELD_MERGEFIELDSINTERNAL = "MergefieldsInternal";
        private const string TEMPLATE_FIELD_BUSINESSOBJECTRECORDTYPE = "BusinessObjectRecordTypeId";
        private const string TEMPLATE_FIELD_TEMPLATEID = "TemplateId";
        private const string TEMPLATE_FIELD_TYPE = "Type";
        private const string QUERYTEMPLATEQUALIFIER_FIELD_OBJECTTYPE = "SobjectType";
        private const string QUERYTEMPLATEQUALIFIER_FIELD_FIELDNAME = "Field";
        private const string QUERYTEMPLATEQUALIFIER_FIELD_COMPARISION_OPERATOR = "ComparisonOperator";
        private const string QUERYTEMPLATEQUALIFIER_FIELD_VALUE = "Value";
        private const string QUERYTEMPLATEFILTER_FIELD_FIELDNAME = "Field";
        private const string QUERYTEMPLATEFILTER_FIELD_COMP_OPERATOR = "ComparisonOperator";
        private const string QUERYTEMPLATEFILTER_FIELD_VALUE = "Value";
        private const string QUERYTEMPLATE_FIELD_OBJECTTYPE = "SobjectType";
        private const string FIELD_RECORDTYPE = "RecordTypeId";
        private const string FIELD_ID = "Id";
        private const string FIELD_NAME = "Name";
        private const string FIELD_ISACTIVE = "Isactive";
        private const string TEMPLATETYPE = "Template";

        private const string EXCEPTION_LOG_EVALUATEQUALIFIER = "DocGen::TemplateManager.EvaluateQualifier:Error while evaluating qualifier of QueryTemplate:{0)} for object:{1}";
        private const string EXCEPTION_LOG_GETTEMPLATEFILTERCONDITION = "DocGen::TemplateManager.GetTemplateFilterCondition:Error while adding filter condition of template:{0}";
        private const string EXCEPTION_LOG_GETACTUALVALUETOCOMPARE = "DocGen::TemplateManager.GetActualValueToCompare:Error while retrieving value of:{0} of object:{1}";

        #endregion Constants

        #region Private Readonly Fields

        private readonly ITemplateRepository _templateRepository;
        private readonly ITemplatePublishUtil _templatePublishUtil;
        private readonly IObjectMetadata objectMetadata;
        private readonly ILogger logger;
        private readonly ApttusRequestContext apiContext;
        private readonly IRecordTypeRepository _recordTypeRepository;

        #endregion Private Readonly Fields

        #region Constructor

        /// <summary>
        /// Instantiate TemplateManager
        /// </summary>
        /// <param name="templateRepository"></param>
        /// <param name="templatePublishUtil"></param>
        /// <param name="objectMetadata"></param>
        /// <param name="apiContext"></param>
        /// <param name="recordTypeRepository"></param>
        public TemplateManager(ITemplateRepository templateRepository, ITemplatePublishUtil templatePublishUtil, IObjectMetadata objectMetadata, ApttusRequestContext apiContext, IRecordTypeRepository recordTypeRepository) {
            _templateRepository = templateRepository;
            _templatePublishUtil = templatePublishUtil;
            this.objectMetadata = objectMetadata;
            logger = PlatformLogger.Factory.CreateLogger<SettingsManager>();
            this.apiContext = apiContext;
            _recordTypeRepository = recordTypeRepository;
        }

        #endregion Constructor

        #region Public Methods

        /// <summary>
        /// Publish Templates
        /// </summary>
        /// <param name="ids">list of template ids</param>
        public async Task Publish(List<string> ids) {
            List<string> fields = new List<string> { TEMPLATE_FIELD_BUSINESSOBJECT, TEMPLATE_FIELD_PUBLISHEDDATE, TEMPLATE_FIELD_NEEDSPUBLISHING, TEMPLATE_FIELD_MERGEFIELDSINTERNAL };
            List<IObjectInstance> templates = await _templateRepository.GetTemplatesById(ids, fields);
            if(templates != null && templates.Count > 0) {
                foreach(var template in templates) {
                }
            }
        }

        /// <summary>
        /// Get templates
        /// </summary>
        /// <param name="queryTemplateDTO"></param>
        /// <param name="queryTemplateType"></param>
        /// <returns></returns>
        public async Task<List<IObjectInstance>> GetTemplates(QueryTemplateDTO queryTemplateDTO) {
            ValidateInput(queryTemplateDTO);

            var templates = new List<IObjectInstance>();
            var metadataList = new Dictionary<string, ObjectMetadataDTO>();
            var objectInstanceList = new Dictionary<string, IObjectInstance>();
            var queryToFetchTemplates = GetQuery();

            var queryTemplates = await _templateRepository.GetQueryTemplatesWithQualifiers(queryTemplateDTO.QueryTemplateType);
            if(queryTemplates?.Count > 0) {
                var qualifiedQueryTemplate = await GetQualifiedQueryTemplate(queryTemplates, queryTemplateDTO.ContextObjectId, metadataList, objectInstanceList);
                if(qualifiedQueryTemplate != null) {
                    queryToFetchTemplates.EntityName = ValidateAndGetObjectType(qualifiedQueryTemplate);
                    await ApplyQueryTemplateFilter(queryTemplateDTO.ContextObjectId, metadataList, objectInstanceList, qualifiedQueryTemplate.GetId(), queryToFetchTemplates);
                }
            }

            if(queryToFetchTemplates.EntityName == OBJECT_TEMPLATE) {
                var recordType = await ValidateAndGetRecordType(queryTemplateDTO, metadataList, objectInstanceList);
                AddTemplateRecordTypeFilter(queryToFetchTemplates, recordType);
                var defaultCriteria = await GetDefaultCriteriaForTemplates(queryTemplateDTO, metadataList);
                queryToFetchTemplates.AddCriteria(defaultCriteria);
            }

            templates = await _templateRepository.GetTemplates(queryToFetchTemplates);
            return templates;
        }

        /// <summary>
        /// Returns the tempalte
        /// </summary>
        /// <param name="id">id of Template</param>
        /// <returns>Template Object</returns>
        public async Task<IObjectInstance> GetTemplateDetails(string id) {
            var template = (await _templateRepository.GetTemplatesById(new List<string> { id }, null)).FirstOrDefault();
            if(template == null) {
                throw new ApplicationException("Template does not exists");
            }
            var recordTypes = await _recordTypeRepository.GetRecordTypesByTemplateId(template.GetId());
            if(recordTypes != null && recordTypes.Count > 0) {
                List<string> recordTypesOfTemplate = new List<string>();
                foreach(var recordType in recordTypes) {
                    recordTypesOfTemplate.Add(recordType.GetName());
                }
                var fields = new Dictionary<string, object>()
                         {{ FIELD_ID, Guid.Empty },
                          { FIELD_NAME, string.Join(", ", recordTypesOfTemplate.ToArray())}};
                var recordTypeInstance = new ObjectInstance(fields, OBJECT_TEMPLATE_RECORDTYPE);
                template.SetValue(TEMPLATE_FIELD_BUSINESSOBJECTRECORDTYPE, recordTypeInstance);
            }
            return template;
        }

        /// <summary>
        /// Returns list of tempaltes for particular Business Object
        /// </summary>
        /// <param name="businessObject"></param>
        /// <returns>List of templates</returns>
        public async Task<List<IObjectInstance>> GetTemplates(string businessObject) {
            var businessObjectKey = await ValidateAndGetBusinessObjectKey(businessObject);
            return await _templateRepository.GetTemplates(businessObjectKey);
        }

        #endregion Public Methods

        #region Private Methods

        private void ValidateInput(QueryTemplateDTO queryTemplateDTO) {
            if(string.IsNullOrEmpty(queryTemplateDTO.ContextObjectId)) {
                throw new ApplicationException($"ObjectId can't be empty");
            }
            if(queryTemplateDTO.QueryTemplateType == default(int)) {
                throw new ApplicationException($"QueryTemplateType Should be Provided");
            }
            if(string.IsNullOrEmpty(queryTemplateDTO.TemplateType)) {
                queryTemplateDTO.TemplateType = TEMPLATETYPE;
            }
        }

        private string ValidateAndGetObjectType(IObjectInstance qualifiedQueryTemplate) {
            var queryTemplateObjectType = qualifiedQueryTemplate.GetValue<SelectOption>(QUERYTEMPLATE_FIELD_OBJECTTYPE);
            if(queryTemplateObjectType == null) {
                throw new ApplicationException($"QueryTemplate [{qualifiedQueryTemplate.GetId()}] should have a valid ObjectType");
            }
            return queryTemplateObjectType.Value;
        }

        private Query GetQuery() {
            var queryToFetchTemplates = new Query(OBJECT_TEMPLATE);
            queryToFetchTemplates.SortOrders = new List<OrderBy> { new OrderBy(FIELD_NAME, SortOrder.ASC) };
            return queryToFetchTemplates;
        }

        private async Task ApplyQueryTemplateFilter(string contextObjectId, Dictionary<string, ObjectMetadataDTO> metadataList, Dictionary<string, IObjectInstance> objectInstanceList, string queryTemplateId, Query queryToFetchTemplates) {
            var queryTemplateFilters = await _templateRepository.GetQueryTemplateFilters(queryTemplateId);
            if(queryTemplateFilters?.Count > 0) {
                var expression = new Expression(ExpressionOperator.AND);
                foreach(var qtFilter in queryTemplateFilters) {
                    var condition = await GetTemplateFilterCondition(contextObjectId, metadataList, objectInstanceList, qtFilter);
                    expression.AddCondition(condition);
                }
                queryToFetchTemplates.AddCriteria(expression);
            }
        }

        private async Task<Condition> GetTemplateFilterCondition(string contextObjectId, Dictionary<string, ObjectMetadataDTO> metadataList, Dictionary<string, IObjectInstance> objectInstanceList, IObjectInstance qtFilter) {
            try {
                var filterOperator = qtFilter.GetValue<SelectOption>(QUERYTEMPLATEFILTER_FIELD_COMP_OPERATOR)?.Key;
                var valueTocompare = qtFilter.GetValue<string>(QUERYTEMPLATEFILTER_FIELD_VALUE);

                var condition = new Condition();
                condition.FieldName = qtFilter.GetValue<string>(QUERYTEMPLATEFILTER_FIELD_FIELDNAME);
                condition.FilterOperator = (FilterOperator)Convert.ToInt32(filterOperator);
                condition.Value = await GetActualValueToCompare(valueTocompare, contextObjectId, metadataList, objectInstanceList);
                return condition;
            } catch(Exception ex) {
                throw new Exception(string.Format(EXCEPTION_LOG_GETTEMPLATEFILTERCONDITION, qtFilter.GetId()), ex);
            }
        }

        private async Task<string> ValidateAndGetRecordType(QueryTemplateDTO queryTemplateDTO, Dictionary<string, ObjectMetadataDTO> metadataList, Dictionary<string, IObjectInstance> objectInstanceList) {
            var recordType = await GetActualValueToCompare($"{queryTemplateDTO.ContextObjectType}.{FIELD_RECORDTYPE}", queryTemplateDTO.ContextObjectId, metadataList, objectInstanceList);
            if(recordType == null) {
                throw new ApplicationException($"{queryTemplateDTO.ContextObjectType}:{queryTemplateDTO.ContextObjectId} is not associated with any record type");
            }
            return recordType.ToString();
        }

        private async Task<string> ValidateAndGetBusinessObjectKey(string businessObject) {
            var businessObjectOption = await GetOptionSetValues(OBJECT_TEMPLATE, TEMPLATE_FIELD_BUSINESSOBJECT);
            var businessObjectKey = businessObjectOption.FirstOrDefault(x => x.Value.Equals(businessObject, StringComparison.InvariantCultureIgnoreCase));
            if(businessObjectKey.Equals(default(KeyValuePair<string, string>))) {
                throw new ApplicationException($"Business Object [{businessObject}] is not valid");
            }

            return businessObjectKey.Key;
        }

        private async Task<Dictionary<string, string>> GetOptionSetValues(string entity, string field) {
            var options = new Dictionary<string, string>();
            try {
                var entityObject = await objectMetadata.GetAsync(entity);
                entityObject.Fields[field].SelectOptionSet.SelectOptions.ForEach(optionSet => options.Add(optionSet.OptionKey, optionSet.OptionValue));
            } catch(Exception ex) {
                logger.LogError("TemplateManager:GetOptionSetValues:{0}", ex.Message);
                throw;
            }

            return options;
        }

        private void AddTemplateRecordTypeFilter(Query queryToFetchTemplates, string recordType) {
            var recordTypeExpression = new Expression(ExpressionOperator.AND);
            recordTypeExpression.AddCondition(new Condition($"{OBJECT_TEMPLATE_RECORDTYPE}.{TEMPLATE_FIELD_BUSINESSOBJECTRECORDTYPE}", FilterOperator.Equal, recordType));
            queryToFetchTemplates.AddJoin(OBJECT_TEMPLATE_RECORDTYPE, FIELD_ID, TEMPLATE_FIELD_TEMPLATEID, JoinType.INNER, OBJECT_TEMPLATE_RECORDTYPE, OBJECT_TEMPLATE, recordTypeExpression);
        }

        private async Task<Expression> GetDefaultCriteriaForTemplates(QueryTemplateDTO queryTemplateDTO, Dictionary<string, ObjectMetadataDTO> metadataList) {
            var templateMetadata = await GetMetadata(OBJECT_TEMPLATE, metadataList);
            var templateType = GetSelectOptionKeyFromValue(templateMetadata, TEMPLATE_FIELD_TYPE, queryTemplateDTO.TemplateType);
            var businessObject = GetSelectOptionKeyFromValue(templateMetadata, TEMPLATE_FIELD_BUSINESSOBJECT, queryTemplateDTO.ContextObjectType);

            var defaultFilters = new Expression(ExpressionOperator.AND);
            defaultFilters.AddCondition(FIELD_ISACTIVE, FilterOperator.Equal, true);
            defaultFilters.AddCondition(TEMPLATE_FIELD_TYPE, FilterOperator.Equal, templateType);
            defaultFilters.AddCondition(TEMPLATE_FIELD_BUSINESSOBJECT, FilterOperator.Equal, businessObject);

            return defaultFilters;
        }

        private string GetSelectOptionKeyFromValue(ObjectMetadataDTO templateMetadata, string fieldName, string valuetoSearch) {
            var fieldMetadata = templateMetadata.Fields[fieldName];
            var selectOption = fieldMetadata?.SelectOptionSet?.SelectOptions?.FirstOrDefault(x => x.OptionValue.Equals(valuetoSearch));
            if(selectOption.Equals(default(KeyValuePair<string, string>))) {
                throw new ApplicationException($"{OBJECT_TEMPLATE}.{fieldName} : [{valuetoSearch}] is not a valid value");
            }
            return selectOption.OptionKey;
        }

        private async Task<object> GetActualValueToCompare(string valueTocompare, string objectId, Dictionary<string, ObjectMetadataDTO> metadataList, Dictionary<string, IObjectInstance> objectInstanceList) {
            try {
                var parsedValues = valueTocompare.Split('.');
                if(parsedValues.Length == 2 && (string.IsNullOrWhiteSpace(parsedValues[0]) || string.IsNullOrWhiteSpace(parsedValues[1]))) {
                    throw new ApplicationException($"[{valueTocompare}] is not valid in Query Template Filter");
                }

                if(parsedValues.Length == 2) {
                    object valueToReturn = default(object);
                    var metadata = await GetMetadata(parsedValues[0], metadataList);
                    var instance = await GetObjectInstance(parsedValues[0], objectInstanceList, objectId, metadataList);
                    FieldMetadataDTO fieldMetadata = metadata.Fields.ContainsKey(parsedValues[1]) ? metadata.Fields[parsedValues[1]] : default(FieldMetadataDTO);
                    if(fieldMetadata == null) {
                        return valueToReturn;
                    }
                    switch(fieldMetadata.Type) {
                        case DataType.Bool:
                            valueToReturn = instance.GetValue<bool>(parsedValues[1]);
                            break;

                        case DataType.Money:
                            valueToReturn = instance.GetValue<decimal>(parsedValues[1]);
                            break;

                        case DataType.Date:
                        case DataType.DateTime:
                            valueToReturn = instance.GetValue<DateTime>(parsedValues[1]);
                            break;

                        case DataType.Decimal:
                            valueToReturn = instance.GetValue<decimal>(parsedValues[1]);
                            break;

                        case DataType.UniqueIdentifier:
                        case DataType.Lookup:
                            valueToReturn = instance.GetValue<string>(parsedValues[1] + ".Id");
                            break;

                        case DataType.Integer:
                        case DataType.MultiSelectOption:
                        case DataType.SelectOption:
                            valueToReturn = instance.GetValue<SelectOption>(parsedValues[1])?.Key;
                            break;

                        case DataType.String:
                        case DataType.LongString:
                            valueToReturn = instance.GetValue<string>(parsedValues[1]);
                            break;
                    }
                    return valueToReturn;
                } else {
                    return valueTocompare;
                }
            } catch(Exception ex) {
                throw new Exception(string.Format(EXCEPTION_LOG_GETACTUALVALUETOCOMPARE, valueTocompare, objectId), ex);
            }
        }

        private async Task<IObjectInstance> GetQualifiedQueryTemplate(List<IObjectInstance> queryTemplates, string objectId, Dictionary<string, ObjectMetadataDTO> metadataList, Dictionary<string, IObjectInstance> objectInstanceList) {
            var distinctQueryTemplateIds = queryTemplates.Select(r => r.GetId()).Distinct();
            await GetMetadata(OBJECT_QUERYTEMPLATEQUALIFIER, metadataList);
            foreach(var queryTemplateId in distinctQueryTemplateIds) {
                var qualifiers = queryTemplates.Where(r => r.GetId() == queryTemplateId);
                var success = true;
                foreach(var qualifier in qualifiers) {
                    success = await EvaluateQualifier(objectId, metadataList, objectInstanceList, qualifier);
                    if(!success) {
                        break;
                    }
                }
                if(success) {
                    return qualifiers.FirstOrDefault();
                }
            }
            return null;
        }

        private async Task<bool> EvaluateQualifier(string objectId, Dictionary<string, ObjectMetadataDTO> metadataList, Dictionary<string, IObjectInstance> objectInstanceList, IObjectInstance qualifier) {
            try {
                var qualifierObjectType = qualifier.GetValue<SelectOption>($"{OBJECT_QUERYTEMPLATEQUALIFIER}.{QUERYTEMPLATEQUALIFIER_FIELD_OBJECTTYPE}");
                if(qualifierObjectType == null) {
                    return false;
                }
                var objectMetadataInfo = await GetMetadata(qualifierObjectType.Value, metadataList);
                var objectInstance = await GetObjectInstance(qualifierObjectType.Value, objectInstanceList, objectId, metadataList);
                var fieldName = qualifier.GetValue<string>($"{OBJECT_QUERYTEMPLATEQUALIFIER}.{QUERYTEMPLATEQUALIFIER_FIELD_FIELDNAME}");
                var ruleOperator = (RuleOperator)Convert.ToInt32(qualifier.GetValue<SelectOption>($"{OBJECT_QUERYTEMPLATEQUALIFIER}.{QUERYTEMPLATEQUALIFIER_FIELD_COMPARISION_OPERATOR}")?.Key);
                var value = qualifier.GetValue<string>($"{OBJECT_QUERYTEMPLATEQUALIFIER}.{QUERYTEMPLATEQUALIFIER_FIELD_VALUE}");
                var Condition = new Tuple<int, string, RuleOperator, string>(1, fieldName, ruleOperator, value);
                return ExpressionEval.ApplyRuleOnBusinessObject(objectInstance, objectMetadataInfo, "1", new List<Tuple<int, string, RuleOperator, string>> { Condition });
            } catch(Exception ex) {
                throw new Exception(string.Format(EXCEPTION_LOG_EVALUATEQUALIFIER, qualifier.GetId(), objectId), ex);
            }
        }

        private async Task<IObjectInstance> GetObjectInstance(string objectType, Dictionary<string, IObjectInstance> objectInstanceList, string objectId, Dictionary<string, ObjectMetadataDTO> metadataList = null) {
            IObjectInstance objectInstance = default(IObjectInstance);
            if(objectInstanceList.ContainsKey(objectType)) {
                objectInstance = objectInstanceList[objectType];
            } else {
                objectInstance = await _templateRepository.GetObject(objectType, await GetObjectIDFromContext(objectType, objectId), metadataList[objectType]);
                if(objectInstance == null) {
                    throw new ApplicationException($"Could not find an object of Type [{objectType}] with the ID [{objectId}]");
                }
                objectInstanceList.Add(objectType, objectInstance);
            }
            return objectInstance;
        }

        private async Task<ObjectMetadataDTO> GetMetadata(string objectType, Dictionary<string, ObjectMetadataDTO> metadataList) {
            ObjectMetadataDTO objectMetadataInfo = default(ObjectMetadataDTO);
            if(metadataList.ContainsKey(objectType)) {
                objectMetadataInfo = metadataList[objectType];
            } else {
                objectMetadataInfo = await objectMetadata.GetAsync(objectType);
                if(objectMetadataInfo == null) {
                    throw new ApplicationException($"[{objectType}] is not a valid Entity");
                }
                metadataList.Add(objectType, objectMetadataInfo);
            }
            return objectMetadataInfo;
        }

        private async Task<string> GetObjectIDFromContext(string objectType, string objectId) {
            if(objectType.Equals(OBJECT_SYSTEMUSER)) {
                UserInfo currentUser = apiContext.UserInfo;
                return await Task.FromResult(Convert.ToString(currentUser.UserId));
            } else if(objectType.Equals(OBJECT_ORGANIZATION)) {
                UserInfo currentUser = apiContext.UserInfo;
                return await Task.FromResult(currentUser.OrganizationId);
            } else {
                return await Task.FromResult(objectId);
            }
        }

        #endregion Private Methods
    }
}
