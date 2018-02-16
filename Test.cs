using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Apttus.Chronos.Client;
using Apttus.Chronos.Common.Api;
using Apttus.Chronos.Common.DTO.Shared;
using Apttus.Chronos.Common.Shared;
using System.Net;
using Apttus.Messaging.Core.Models.Common;

namespace Apttus.DocGen.Domain.Util
{
    public class ChronosClient : IChronosClient
    {

        private ChronosConfig _config;
        private string _tenantId;

        public ChronosClient(string apiEndpoint, string tenantId, string clientId, string clientSecret, bool enableHttps)
        {
            _config = new ChronosConfig();
            _config.ApiEndPoint = apiEndpoint;
            _config.ClientId = clientId;
            _config.ClientSecret = clientSecret;
            _config.EnableHttps = enableHttps;
            _config.TenantId = tenantId;
            this._tenantId = tenantId;
            _config.Validate();
        }


        public async Task<MacroApiResponse> createChronosJobAsync(string macroName, string eventMessage, string serviceName, string eventType, string cName,
            string callbackEndpoint, object data, int interval, string frequency, DateTime startTime)
        {

            //TenantClient tenantclient = new TenantClient(_config);
            //var isAppRegistered = tenantclient.GetTenant().Result;
            //bool onboardTenant = isAppRegistered.StatusCode == HttpStatusCode.OK ? false : true;

            //if (onboardTenant)
            //{
            //    TenantApiRequest apirequest = new TenantApiRequest();
            //    apirequest.TenantRequest.SubDomain = _tenantId;
            //    TenantApiResponse respCreate = tenantclient.OnboardTenant(apirequest).Result;
            //    TenantApiResponse response = tenantclient.GetTenant().Result;
            //}

            MacroClient macroClient = new MacroClient(_config);
            MacroApiRequest apiRequest = new MacroApiRequest();
            apiRequest.MacroName = macroName;
            apiRequest.CronExpression.StartTime = DateTime.Now;
            apiRequest.EventMessage = new EventMessage(eventMessage);
            apiRequest.EventMessage.CorrelationId = Guid.NewGuid().ToString();
            apiRequest.EventMessage.TenantId = _tenantId;
            apiRequest.EventMessage.Source = serviceName;
            apiRequest.EventMessage.Type = eventType + macroName;

            //Settings for callback action on API router                
            apiRequest.EventMessage.CName = cName;

            Dictionary<string, string> addHeaders = new Dictionary<string, string>();
           
            apiRequest.MacroActions.Add(new MacroAction()
            {
                ActionType = Apttus.Chronos.Common.Enum.ActionType.NOTIFYAPIROUTER, //TODO: change to NotifyApiRouter.
                CallbackAction = new CallbackAction()
                {
                    CallbackEndpoint = callbackEndpoint,
                    CallbackData = data
                }
            });

            apiRequest.CronExpression.Interval = interval;
            apiRequest.CronExpression.Frequency = frequency;
            apiRequest.CronExpression.StartTime = startTime;
            apiRequest.Validate();

            MacroApiResponse resp = new MacroApiResponse();
            //var getMacro = await macroClient.GetMacro(macroName);
            //if (getMacro.StatusCode != HttpStatusCode.OK)
            //{
                resp = await macroClient.CreateMacro(apiRequest);
            //}
            //else
            //{
            //    resp = await macroClient.UpdateMacro(apiRequest.MacroName, apiRequest); // Updates existing macro
            //}

            return resp;

        }

        public async Task<MacroApiResponse> UpdateMacroAsync(string macroName, MacroApiRequest apiRequest)
        {
            MacroClient macroClient = new MacroClient(_config);
            return await macroClient.UpdateMacro(macroName, apiRequest);
        }


        public async Task<MacroApiResponse> TriggerMacroAsync(string macroName)
        {
            MacroClient macroClient = new MacroClient(_config);
            return await macroClient.TriggerMacro(macroName);
        }

        public async Task<MacroApiResponse> GetAllTenantMacroAsync(string macroName)
        {
            MacroClient macroClient = new MacroClient(_config);
            return await macroClient.GetMacro();
        }

        public async Task<MacroApiResponse> GetMacroAsync(string macroName)
        {
            MacroClient macroClient = new MacroClient(_config);
            return await macroClient.GetMacro(macroName);
        }

        public async Task<MacroApiResponse> GetMacroHistoryAsync(string macroName)
        {
            MacroClient macroClient = new MacroClient(_config);
            return await macroClient.GetMacroHistory(macroName);
        }

        public async Task<MacroApiResponse> DeleteMacroAsync(string macroName)
        {
            MacroClient macroClient = new MacroClient(_config);
            return await macroClient.DeleteMacro(macroName);
        }

    }
}
