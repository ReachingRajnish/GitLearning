
﻿using Apttus.DataAccess.Common.Enums;
using Apttus.DataAccess.Common.Interface;
using Apttus.DataAccess.Common.Model;
using Apttus.DocGen.Model.Constants;
using Apttus.DocGen.DAL.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apttus.DocGen.DAL.Implementations {
    public class KiraIDERepository : IIDERepository {


        private readonly IDataRepository _dataAccessRepository;
        private static object lockObject = new object();

        /// <summary>
        /// CTOR
        /// </summary>
        /// <param name="dataAccessRepository">DataAccess REPO</param>
        public KiraIDERepository(IDataRepository dataAccessRepository) {
            _dataAccessRepository = dataAccessRepository;
        }

        /// <summary>
        /// Upserting
        /// </summary>
        /// <param name="fields">Fields using Dictionary</param>
        /// <returns></returns>
        public async Task<IObjectInstance> UpsertIDEJobAsync(Dictionary<string, object> fields) {
            IObjectInstance objectInst = new ObjectInstance(fields, IDEConstants.IDEJOBS_ENTITYNAME);
            if(string.IsNullOrEmpty(objectInst.GetId())) {
                await _dataAccessRepository.InsertAsync(objectInst);
            } else {
                await _dataAccessRepository.UpdateAsync(objectInst);
            }
            return await Task.FromResult<IObjectInstance>(objectInst);
        }

        /// <summary>
        /// Getting IDE Job
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        public IObjectInstance GetIdeJob(string jobId) {
            Query queryIDEJobs = new Query(IDEConstants.IDEJOBS_ENTITYNAME);
            queryIDEJobs.AddColumns(IDEConstants.IDEJOBS_FIELD_AGREEMENTID, IDEConstants.IDEJOBS_FIELD_NAME, IDEConstants.IDEJOBS_FIELD_JOBDETAIL,
                IDEConstants.IDEJOBS_FIELD_JOBTYPE, IDEConstants.IDEJOBS_FIELD_JOBSTATUS, IDEConstants.IDEJOBS_FIELD_ID, IDEConstants.IDEJOBS_FIELD_MUTEX);
            queryIDEJobs.AddCriteria(IDEConstants.IDEJOBS_FIELD_ID, FilterOperator.Equal, jobId);
            queryIDEJobs.TopRecords = 1;

            return _dataAccessRepository.GetRecordsAsync(queryIDEJobs).Result.FirstOrDefault<IObjectInstance>();
        }

        /// <summary>
        /// Monitor Enter- Transaction Selecting and updating DB record
        /// </summary>
        /// <returns></returns>
        public IObjectInstance EnterIDEJobForChronos() {
            IObjectInstance ideJob = null;
            bool ideUpdated = false;

            //Locking Code for transaction : Select Mutex == 0; and update mutex +=1;
            lock(lockObject) {
                Query queryIDEJobs = new Query(IDEConstants.IDEJOBS_ENTITYNAME);
                queryIDEJobs.AddColumns(IDEConstants.IDEJOBS_FIELD_AGREEMENTID, IDEConstants.IDEJOBS_FIELD_NAME, IDEConstants.IDEJOBS_FIELD_JOBDETAIL,
                    IDEConstants.IDEJOBS_FIELD_JOBTYPE, IDEConstants.IDEJOBS_FIELD_JOBSTATUS, IDEConstants.IDEJOBS_FIELD_ID, IDEConstants.IDEJOBS_FIELD_MUTEX);
                queryIDEJobs.AddCriteria(IDEConstants.IDEJOBS_FIELD_MUTEX, FilterOperator.LessEqual, 0);
                queryIDEJobs.AddCriteria(IDEConstants.IDEJOBS_FIELD_JOBTYPE, FilterOperator.Equal, KIRAConstants.IDEJOBS_JOBTYPE_OCREXTRACTION);
                queryIDEJobs.AddCriteria(IDEConstants.IDEJOBS_FIELD_JOBSTATUS, FilterOperator.Equal, KIRAConstants.IDEJOBS_JOBSTATUS_PENDING);
                queryIDEJobs.TopRecords = 1;

                ideJob = _dataAccessRepository.GetRecordsAsync(queryIDEJobs).Result.FirstOrDefault<IObjectInstance>();

                if(ideJob != null) {

                    int mutexValue = ideJob.GetValue<int>(IDEConstants.IDEJOBS_FIELD_MUTEX);
                    if(mutexValue.Equals(null)) {
                        mutexValue = 0;
                    }

                    Dictionary<string, object> fields = new Dictionary<string, object>();
                    fields.Add(IDEConstants.IDEJOBS_FIELD_ID, ideJob.GetId());
                    fields.Add(IDEConstants.IDEJOBS_FIELD_MUTEX, mutexValue + 1);

                    IObjectInstance objectInst = new ObjectInstance(fields, IDEConstants.IDEJOBS_ENTITYNAME);
                    ideUpdated = _dataAccessRepository.UpdateAsync(objectInst).Result;


                }

            }

            if(ideJob != null && ideUpdated) {
                return ideJob;
            } else {
                return null;
            }

        }

        /// <summary>
        /// Exiting Monitor - Transacting DB Record
        /// </summary>
        /// <param name="ideJobId"></param>
        /// <returns></returns>
        public bool ExitIDEJobForChronos(string ideJobId) {

            bool ideUpdated = false;

            //Locking Code for transaction : update mutex -=1;
            lock(lockObject) {
                Query queryIDEJobs = new Query(IDEConstants.IDEJOBS_ENTITYNAME);
                queryIDEJobs.AddColumns(IDEConstants.IDEJOBS_FIELD_ID, IDEConstants.IDEJOBS_FIELD_MUTEX);
                queryIDEJobs.AddCriteria(IDEConstants.IDEJOBS_FIELD_ID, FilterOperator.Equal, ideJobId);
                queryIDEJobs.TopRecords = 1;

                IObjectInstance ideJob = _dataAccessRepository.GetRecordsAsync(queryIDEJobs).Result.FirstOrDefault<IObjectInstance>();

                if(ideJob != null) {

                    int mutexValue = ideJob.GetValue<int>(IDEConstants.IDEJOBS_FIELD_MUTEX);

                    Dictionary<string, object> fields = new Dictionary<string, object>();
                    fields.Add(IDEConstants.IDEJOBS_FIELD_ID, ideJob.GetId());
                    fields.Add(IDEConstants.IDEJOBS_FIELD_MUTEX, mutexValue - 1);

                    if(mutexValue - 1 != 0) //Binary Mutex Only.
                    {
                        throw new Exception("Cross threading - IDEJobs are in invalid stage");
                    }

                    IObjectInstance objectInst = new ObjectInstance(fields, IDEConstants.IDEJOBS_ENTITYNAME);
                    ideUpdated = _dataAccessRepository.UpdateAsync(objectInst).Result;
                }

            }

            return ideUpdated;
        }
    }
}
