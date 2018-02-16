using Apttus.DataAccess.Common.Interface;
using Apttus.DocGen.DAL.Interfaces;
using Apttus.DocGen.Domain.Interfaces;
using Apttus.DocGen.Domain.Util;
using Apttus.DocGen.Model.DTO;
using Apttus.DocGen.Model.Constants;
using Apttus.Security.Common.Authentication.DTO.RequestContext;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Apttus.DocGen.Domain.Implementations {

    public class KiraProvider : IIDEProvider {
        private readonly ApttusRequestContext _apiContext;
        private readonly RestClient _restClient;
        private readonly ILogger _log;
        private readonly IProductSettingUtil _productSettingUtil;
        private readonly IEmailUtil _emailUtil;
        private readonly IIDERepository _ideRepository;

        #region Constants
        //TODO: get it from Product Setting 
        private const string API_VERSION = "v1";
        private const string API_URL = "https://us.app.kirasystems.com/platform-api";
        private const string AUTH_TOKEN = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzIjoiQlVqY3Q5eEhSWHBtVG5iMHEyOEJjWnNsIiwiZiI6MTcyfQ.CngXVvK17MbWuxUPqli5SlzuyFVDStf8-2REbNpC9UQ";

        private const string REST_GET_METHOD = "GET";

        #endregion


        #region Public Methods
        /// <summary>
        /// Kira Provider CTOR
        /// </summary>
        /// <param name="apiContext"></param>
        /// <param name="ideRepository"></param>
        /// <param name="logger"></param>
        /// <param name="productSettingHelper"></param>
        /// <param name="emailHelper"></param>
        /// <param name="restClient"></param>
        public KiraProvider(ApttusRequestContext apiContext, IIDERepository ideRepository, ILogger logger, IProductSettingUtil productSettingHelper,
            IEmailUtil emailHelper, RestClient restClient) {
            this._apiContext = apiContext;
            this._ideRepository = ideRepository;
            this._log = logger;
            this._productSettingUtil = productSettingHelper;
            this._emailUtil = emailHelper;

            restClient._authToken = AUTH_TOKEN;
            this._restClient = restClient;

        }

        /// <summary>
        /// Upload Document to KIRA 
        /// </summary>
        /// <param name="files">Filename, type and byte[] </param>
        /// <param name="recordTypeId">optional record type id</param>
        /// <param name="agreementId">optional agreement id</param>
        /// <returns>IDE Job details</returns>
        public async Task<List<IObjectInstance>> UploadDocuments(List<Tuple<string, string, byte[]>> files, string recordTypeId, string agreementId) {
            var transientDocuments = new List<IObjectInstance>();
            foreach(var file in files) {
                //TODO: parse project id according to recordtype id from Product/admin setting.
                var uploadDoc = await UploadTransientDocumentAsync(file.Item1, file.Item2, file.Item3, 1111, agreementId);
                transientDocuments.Add(uploadDoc);
            }
            return transientDocuments;
        }

        /// <summary>
        /// Get Job Status for particular KIRA Job
        /// </summary>
        /// <param name="id">IDE Job ID - GUID</param>
        /// <returns></returns>
        public async Task<string> GetJobStatusAsync(string id) {
            IObjectInstance ideJob = _ideRepository.GetIdeJob(id);

            if(ideJob != null) {

                string jobType = ideJob.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBTYPE);
                string jobStatus = ideJob.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBSTATUS);

                if(jobType == KIRAConstants.IDEJOBS_JOBTYPE_OCREXTRACTION) {
                    string jobDetail = ideJob.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBDETAIL);
                    KiraDTOs.JobDetailDB jobDetailDB = JsonConvert.DeserializeObject<KiraDTOs.JobDetailDB>(jobDetail);

                    return await GetJobStatusAsync(jobDetailDB.jobId);
                } else if(jobType == KIRAConstants.IDEJOBS_JOBTYPE_FILEDOWNLOAD) {
                    throw new Exception("Extraction Job is moved to file-download.");
                } else {
                    throw new Exception("Job ID is invalid.");
                }
            } else {
                throw new Exception("IDE Job does not exist.");
            }
        }

        /// <summary>
        /// Get Analysis. Fields and Clauses
        /// </summary>
        /// <param name="id">IDE Job ID</param>
        /// <returns></returns>
        public async Task<string> GetExtractionAsync(string id) {
            IObjectInstance ideJob = _ideRepository.GetIdeJob(id);
            if(ideJob != null) {
                string jobType = ideJob.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBTYPE);
                string jobStatus = ideJob.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBSTATUS);

                if(jobType == KIRAConstants.IDEJOBS_JOBTYPE_FILEDOWNLOAD || jobType == KIRAConstants.IDEJOBS_JOBTYPE_OCREXTRACTION) {
                    string jobDetail = ideJob.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBDETAIL);
                    KiraDTOs.JobDetailDB jobDetailDB = JsonConvert.DeserializeObject<KiraDTOs.JobDetailDB>(jobDetail);

                    return await GetExtractionAsync(jobDetailDB.documentId);
                } else {
                    throw new Exception("Job ID is invalid");
                }
            } else {
                throw new Exception("IDE Job does not exist");
            }
        }

        /// <summary>
        /// Get Analysed File
        /// </summary>
        /// <param name="id">IDE Job ID</param>
        /// <returns></returns>
        public async Task<byte[]> GetAnalysedFile(string id) {
            IObjectInstance ideJob = _ideRepository.GetIdeJob(id);
            if(ideJob != null) {
                string jobType = ideJob.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBTYPE);
                string jobStatus = ideJob.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBSTATUS);

                if(jobType == KIRAConstants.IDEJOBS_JOBTYPE_FILEDOWNLOAD) {
                    string jobDetail = ideJob.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBDETAIL);
                    KiraDTOs.JobDetailDB jobDetailDB = JsonConvert.DeserializeObject<KiraDTOs.JobDetailDB>(jobDetail);

                    return await GetKiraFileFromURLAsync(jobDetailDB.exportJobStatusURL);
                } else if(jobType == KIRAConstants.IDEJOBS_JOBTYPE_OCREXTRACTION) {
                    throw new Exception("Job is still in progress");
                } else {
                    throw new Exception("Job ID is invalid");
                }
            } else {
                throw new Exception("IDE Job does not exist");
            }
        }

        /// <summary>
        /// CHORONS Job. With granular Mutex at DB row level.
        /// </summary>
        /// <returns></returns>
        public async Task ChoronosJobAsync() {
            IObjectInstance ideRecord = null;
            do {
                ideRecord = _ideRepository.EnterIDEJobForChronos();

                if(ideRecord != null) {
                    try {
                        await CheckForIDEExtractionJob(ideRecord, true);
                    } catch(Exception ex) {
                        _log.LogError("KIRAProvider::ChoronosJobAsync - ", ex);
                    } finally {
                        _ideRepository.ExitIDEJobForChronos(ideRecord.GetId());
                    }
                } else {
                    break;
                }

            } while(ideRecord != null);
        }


        /// <summary>
        /// Check Job status and Update it.
        /// </summary>
        /// <param name="id">IDE Job ID</param>
        /// <returns></returns>
        public async Task CheckAndUpdateIDEJob(string id) {
            IObjectInstance ideJob = _ideRepository.GetIdeJob(id);
            await CheckForIDEExtractionJob(ideJob, false);
        }
        #endregion


        #region Private Methods


        /// <summary>
        /// Checking Extraction Job 
        /// </summary>
        /// <param name="ideRecord">IDERecord IObj</param>
        /// <param name="sendMail">Send Mail or Not</param>
        /// <returns></returns>
        private async Task<bool> CheckForIDEExtractionJob(IObjectInstance ideRecord, bool sendMail) {
            try {
                string jobType = ideRecord.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBTYPE);
                string jobStatus = ideRecord.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBSTATUS);

                if(jobType == KIRAConstants.IDEJOBS_JOBTYPE_OCREXTRACTION && jobStatus == KIRAConstants.IDEJOBS_JOBSTATUS_PENDING) {
                    string jobDetail = ideRecord.GetValue<string>(IDEConstants.IDEJOBS_FIELD_JOBDETAIL);
                    KiraDTOs.JobDetailDB jobDetailDb = JsonConvert.DeserializeObject<KiraDTOs.JobDetailDB>(jobDetail);

                    string strJobStatus = await GetJobStatusAsync(jobDetailDb.jobId);
                    KiraDTOs.JobStatus extractionJobStatus = JsonConvert.DeserializeObject<KiraDTOs.JobStatus>(strJobStatus);

                    if(extractionJobStatus.job_status == KIRAConstants.IDEJOBS_JOBSTATUS_COMPLETED) {
                        string exportJobStatusURL = await CreateDocumentExportJob(jobDetailDb.documentId);

                        //TODO: Template with Redirect URL to review page
                        if(sendMail) {
                            await _emailUtil.SendEmail("This is just testing " + ideRecord.GetId(), "test", "Dhaval", "dgajera@apttus.com", "Success");
                        }


                        jobDetailDb.exportJobStatusURL = exportJobStatusURL;
                        Dictionary<string, object> jobUpdate = new Dictionary<string, object>();
                        jobUpdate.Add(IDEConstants.IDEJOBS_FIELD_ID, ideRecord.GetId());
                        jobUpdate.Add(IDEConstants.IDEJOBS_FIELD_JOBDETAIL, JsonConvert.SerializeObject(jobDetailDb));
                        jobUpdate.Add(IDEConstants.IDEJOBS_FIELD_JOBSTATUS, KIRAConstants.IDEJOBS_JOBSTATUS_PENDING);
                        jobUpdate.Add(IDEConstants.IDEJOBS_FIELD_JOBTYPE, KIRAConstants.IDEJOBS_JOBTYPE_FILEDOWNLOAD);

                        var ideJob = await _ideRepository.UpsertIDEJobAsync(jobUpdate);

                        if(ideJob.GetId() == ideRecord.GetId()) {
                            return true;
                        } else {
                            return false;
                        }
                    } else {
                        return false;
                    }
                } else if(jobType == KIRAConstants.IDEJOBS_JOBTYPE_FILEDOWNLOAD) {
                    return true;
                } else {
                    return false;
                }
            } catch(Exception ex) {
                //loggin error as executing in CHRONOS Job.
                _log.LogError("KIRAProvider::CheckForIDEExtractionJob - ", ex);
                return false;
            }
        }

        /// <summary>
        /// KIRA API call for getting Job Status
        /// </summary>
        /// <param name="jobId">KIRA Job ID</param>
        /// <returns></returns>
        private async Task<string> GetJobStatusAsync(uint jobId) {
            var requestUrl = string.Format(KIRAConstants.IDEJOBS_URL_JOBSTATUS, API_URL, API_VERSION, jobId);
            return await GetJobStatusByURLAsync(requestUrl);
        }

        /// <summary>
        /// KIRA API Call for getting Job status by URL
        /// </summary>
        /// <param name="requestUrl">KIRA Job Status URL</param>
        /// <returns></returns>
        private async Task<string> GetJobStatusByURLAsync(string requestUrl) {
            return await _restClient.ExecuteRestAsync(requestUrl, REST_GET_METHOD);
        }

        /// <summary>
        /// KIRA API Call for Getting Fields and Clauses 
        /// </summary>
        /// <param name="documentId">KIRA Document ID</param>
        /// <returns></returns>
        private async Task<string> GetExtractionAsync(uint documentId) {
            var requestUrl = string.Format(KIRAConstants.IDEJOBS_URL_FIELDEXTRACTIONS, API_URL, API_VERSION, documentId);

            return await _restClient.ExecuteRestAsync(requestUrl, REST_GET_METHOD);
        }


        /// <summary>
        /// Create  KIRA File Export job 
        /// </summary>
        /// <param name="documentId">document ID</param>
        /// <returns></returns>
        private async Task<string> CreateDocumentExportJob(uint documentId) {
            var requestUrl = string.Format(KIRAConstants.IDEJOBS_URL_DOCEXPORT, API_URL, API_VERSION, documentId);

            string DownloadJObURL = await _restClient.PostRequestForContentLocationAsync(requestUrl, AUTH_TOKEN,
                KIRAConstants.IDEJOBS_URL_DOCEXPORT_PAYLOAD);
            return DownloadJObURL;
        }


        /// <summary>
        /// Retrieve KIRA Extracted ZIP File 
        /// </summary>
        /// <param name="URL">KIRA Export Job Status URL</param>
        /// <returns></returns>
        private async Task<byte[]> GetKiraFileFromURLAsync(string URL) {

            byte[] filec = null;
            string DownloadJobStatusJSON = await _restClient.ExecuteRestAsync(URL, REST_GET_METHOD);
            KiraDTOs.ExportFileJobStatus exportJobStatus = JsonConvert.DeserializeObject<KiraDTOs.ExportFileJobStatus>(DownloadJobStatusJSON);

            if(exportJobStatus.is_successful) {
                string fileDetails = await _restClient.ExecuteRestAsync(exportJobStatus.redirect_href, REST_GET_METHOD);
                List<KiraDTOs.DownloadFileStatus> dfileDetails = JsonConvert.DeserializeObject<List<KiraDTOs.DownloadFileStatus>>(fileDetails);

                if(dfileDetails.Count > 0) {
                    KiraDTOs.DownloadFileStatus FileStatus = dfileDetails[0];

                    if(FileStatus.status == KIRAConstants.IDEJOBS_FILEJOBSTATUS_FINISHED) {
                        WebClient x = new System.Net.WebClient();
                        x.Headers.Add("Authorization", "Bearer " + AUTH_TOKEN);
                        filec = x.DownloadData(exportJobStatus.redirect_href + "/" + FileStatus.file_id);
                        return filec;
                    } else {
                        throw new Exception("file is not ready for download");
                    }

                } else {
                    throw new Exception("file is not ready for download");
                }
            } else {
                throw new Exception("File Export Job is not completed");
            }

        }

        /// <summary>
        /// Upload Doc to KIRA for Process
        /// </summary>
        /// <param name="fileName">filename</param>
        /// <param name="contentType">MimeType</param>
        /// <param name="file">Binary byte[]</param>
        /// <param name="projectID">Project ID</param>
        /// <param name="agreementId">Agreement ID(optional)</param>
        /// <returns></returns>
        private async Task<IObjectInstance> UploadTransientDocumentAsync(string fileName, string contentType, byte[] file,
            uint projectID, string agreementId) {

            var requestUrl = string.Format(KIRAConstants.IDEJOBS_URL_UPLOADDOC, API_URL, API_VERSION);
            Dictionary<string, object> postParameters = new Dictionary<string, object>();
            postParameters.Add(KIRAConstants.IDEJOBS_URL_UPLOADDOC_PAYLOAD, projectID);

            string jobDetailJson = await _restClient.uploadFile(requestUrl, postParameters, file, fileName, contentType);


            Dictionary<string, object> ideJob = new Dictionary<string, object>();
            if(!string.IsNullOrEmpty(agreementId)) {
                ideJob.Add(IDEConstants.IDEJOBS_FIELD_AGREEMENTID, agreementId);
            }

            KiraDTOs.ExtractionJobDetail extractionJobDetail = JsonConvert.DeserializeObject<KiraDTOs.ExtractionJobDetail>(jobDetailJson);
            KiraDTOs.JobDetailDB jobDetailDB = new KiraDTOs.JobDetailDB() { documentId = extractionJobDetail.document_id, jobId = extractionJobDetail.job_id };

            ideJob.Add(IDEConstants.IDEJOBS_FIELD_NAME, KIRAConstants.IDEJOBS_NAME_PREFIX + extractionJobDetail.document_id);
            ideJob.Add(IDEConstants.IDEJOBS_FIELD_JOBDETAIL, JsonConvert.SerializeObject(jobDetailDB));
            ideJob.Add(IDEConstants.IDEJOBS_FIELD_JOBTYPE, KIRAConstants.IDEJOBS_JOBTYPE_OCREXTRACTION);
            ideJob.Add(IDEConstants.IDEJOBS_FIELD_JOBSTATUS, KIRAConstants.IDEJOBS_JOBSTATUS_PENDING);

            IObjectInstance ideJobObjInstance = await UpsertIDEJob(ideJob);
            return ideJobObjInstance;

        }

        private async Task<IObjectInstance> UpsertIDEJob(Dictionary<string, object> ideJob) {
            var ideRecord = await _ideRepository.UpsertIDEJobAsync(ideJob);
            return ideRecord;
        }

        #endregion
    }
