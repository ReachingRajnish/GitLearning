        private async Task<List<IObjectInstance>> GetActiveRecordTypes(List<string> tobeMappedRecordTypeIds) {
            var fields = new List<string> { FIELD_ID, FIELD_NAME, FIELD_RECORDTYPE_ACTIVE };
            var racordTypes = await recordTypeRepository.GetRecordTypesByIds(tobeMappedRecordTypeIds, fields);
            var activeRecordTypes = racordTypes?.Where(x => x.GetValue<bool>(FIELD_RECORDTYPE_ACTIVE));
            return activeRecordTypes?.ToList();
        }

        private async Task MapRecordTypeAssociatedWithClauses(string obligationId, List<IObjectInstance> clauseRecordTypes) {
            var obligationRecordTypes = await obligationAdminRepository.GetReferences(OBJECT_OBLIGATION_RECORDTYPE, new List<string> { obligationId }, FIELD_OBLIGATIONID, FIELD_RECORDTYPEID);
            var tobeMappedRecordTypes = GetTobeMappedRecordTypes(obligationRecordTypes, clauseRecordTypes);
            if(tobeMappedRecordTypes.Any()) {
                var tobeMappedRecordTypeIds = tobeMappedRecordTypes.Select(x => x.GetValue<IObjectInstance>(FIELD_RECORDTYPEID))
                     .GroupBy(x => x.GetId())
                     .Select(y => y.FirstOrDefault().GetId()).ToList();
                var activeRecordTypes = await GetActiveRecordTypes(tobeMappedRecordTypeIds);
                await obligationAdminRepository.CreateObligationReference(obligationId, activeRecordTypes, OBJECT_OBLIGATION_RECORDTYPE, FIELD_RECORDTYPEID);
            }
        }
