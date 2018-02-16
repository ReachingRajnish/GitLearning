
        /// <summary>
        /// Gets List of Recordtypes.
        /// </summary>
        /// <param name="ids">List of Ids</param>
        /// <param name="fields">List of Fields to be retrieved</param>
        /// <returns></returns>
        public async Task<List<IObjectInstance>> GetRecordTypesByIds(List<string> ids, List<string> fields) {
            var time = System.Diagnostics.Stopwatch.StartNew();
            var query = new Query(APTTUS_OBJECT_RECORDTYPE);
            if(fields != null && fields.Any()) {
                query.AddColumns(fields.ToArray());
            }
            query.Criteria = new Expression(ExpressionOperator.AND);
            query.Criteria.AddCondition(new Condition(CommonEntityFields.NAME, FilterOperator.In, ids));
            var tempateRecord = await dataAccessRepository.GetRecordsAsync(query);
            log.LogTrace("TemplateRepository:GetTemplatesByIds:TotalTimeTaken{0}", time.ElapsedMilliseconds.ToString());
            return tempateRecord?.ToList();
        }
