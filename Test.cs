using Apttus.Contracts.DAL.Implementations;
using Apttus.Contracts.DAL.Interfaces;
using Apttus.Contracts.Model.Enums;
using Apttus.Contracts.UnitTest.Common;
using Apttus.DataAccess.Common.CustomTypes;
using Apttus.DataAccess.Common.Interface;
using Apttus.DataAccess.Common.Model;
using Apttus.Metadata.Client.Interface;
using Apttus.Metadata.Common.DTO.Runtime.V1;
using Apttus.Security.Common.Authentication.DTO.RequestContext;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apttus.Contracts.DAL.UnitTest.Implementations.AzureSQL {
    public class DocumentVersionRepositoryTestsBase {

        protected Mock<ApttusRequestContext> mockApttusRequestContext;
        protected Mock<ILogger> mockLogger;
        protected Mock<IDataRepository> mockDataRepository;
        protected Mock<IObjectMetadata> mockObjectMetadata;
        protected Mock<IAttachmentRepository> mockAttachmentRepository;
        protected DocumentVersionRepository documentVersionRepository;

        [TestInitialize]
        public void Inttalize() {
            mockApttusRequestContext = new Mock<ApttusRequestContext>();
            mockLogger = new Mock<ILogger>();
            mockDataRepository = new Mock<IDataRepository>();
            mockObjectMetadata = new Mock<IObjectMetadata>();
            mockAttachmentRepository = new Mock<IAttachmentRepository>();

            documentVersionRepository = new DocumentVersionRepository();
            documentVersionRepository.apiContext = mockApttusRequestContext.Object;
            documentVersionRepository.log = mockLogger.Object;
            documentVersionRepository.dataAccessRepository = mockDataRepository.Object;
            documentVersionRepository.objectMetadata = mockObjectMetadata.Object;
            documentVersionRepository.attachmentRepository = mockAttachmentRepository.Object;
        }
    }

    public class DocumentVersionRepositoryTests {
        [TestClass]
        public class GetVersionDetailsTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Return_List_Of_Version_Details_With_Valid_Agreement_Id() {
                //Arrange
                List<string> fields = new List<string>();
                fields.Add("Title");
                List<IObjectInstance> versionDetails = new List<IObjectInstance>();
                mockDataRepository
                    .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                    .ReturnsAsync(versionDetails);

                //Act
                List<IObjectInstance> result = await documentVersionRepository.GetVersionDetails(It.IsAny<string>(), fields);

                //Assert
                mockDataRepository
                    .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Once);
                Assert.AreEqual(versionDetails.Count, result.Count);
            }
        }

        [TestClass]
        public class GetDocumentVersionsByAgreementIDTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            [ExpectedException(typeof(ApplicationException))]
            public async Task Should_Throw_Exception_When_Fields_Count_Zero() {
                //Arrange
                List<string> fields = new List<string>();

                //Act
                List<IObjectInstance> result = await documentVersionRepository.GetDocumentVersionsByAgreementID(It.IsAny<string>(), fields);
            }

            [TestMethod]
            public async Task Should_Return_Document_Versions_With_Valid_Agreement_Id() {
                //Arrange
                List<string> fields = new List<string>();
                fields.Add("Title");
                List<IObjectInstance> versionDetails = new List<IObjectInstance>();
                mockDataRepository
                    .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                    .ReturnsAsync(versionDetails);

                //Act
                List<IObjectInstance> result = await documentVersionRepository.GetDocumentVersionsByAgreementID(It.IsAny<string>(), fields);

                //Assert
                mockDataRepository
                    .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Once);
                Assert.AreEqual(versionDetails.Count, result.Count);
            }
        }

        [TestClass]
        public class CreateVersionDetailTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Return_Version_Details_With_Template_Id_Null() {
                //Arrange
                ObjectMetadataDTO metadata = new ObjectMetadataDTO();
                Dictionary<string, FieldMetadataDTO> fields = new Dictionary<string, FieldMetadataDTO>();
                FieldMetadataDTO documentSecurity = new FieldMetadataDTO() { DefaultValue = "DefaultValue" };
                fields.Add("DocumentSecurity", documentSecurity);
                metadata.Fields = fields;
                List<IObjectInstance> versions = new List<IObjectInstance>();
                IObjectInstance docVersion = new MockObjectInstance();
                docVersion.SetValue("NumberOfVersions", decimal.One);
                docVersion.SetValue("Id", Guid.NewGuid().ToString());
                string versionDetailId = Guid.NewGuid().ToString();
                mockDataRepository
                    .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                    .ReturnsAsync(versions);
                mockDataRepository
                  .Setup(s => s.InsertAsync(It.IsAny<IObjectInstance>()))
                  .ReturnsAsync(versionDetailId);
                mockDataRepository
                  .Setup(s => s.GetObjectByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<bool>()))
                  .ReturnsAsync(docVersion);
                mockObjectMetadata
                    .Setup(s => s.GetAsync(It.IsAny<string>()))
                    .ReturnsAsync(metadata);
                //Act
                IObjectInstance result = await documentVersionRepository.CreateVersionDetail(It.IsAny<string>(),
                                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentVersionType>(), It.IsAny<SelectOption>());

                //Assert
                mockDataRepository
                    .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Once);
                mockDataRepository
                  .Verify(s => s.InsertAsync(It.IsAny<IObjectInstance>()), Times.Exactly(2));
                mockDataRepository
                 .Verify(s => s.GetObjectByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<bool>()), Times.Once);
                mockObjectMetadata
                  .Verify(s => s.GetAsync(It.IsAny<string>()), Times.Once);
                Assert.IsNotNull(result);
            }
            [TestMethod]
            public async Task Should_Return_Version_Details_With_Valid_Template_Id() {
                //Arrange
                string templateId = Guid.NewGuid().ToString();
                List<IObjectInstance> versions = CreateVersions();
                string versionDetailId = Guid.NewGuid().ToString();
                ObjectMetadataDTO metadata = new ObjectMetadataDTO();
                Dictionary<string, FieldMetadataDTO> fields = new Dictionary<string, FieldMetadataDTO>();
                FieldMetadataDTO documentSecurity = new FieldMetadataDTO() { DefaultValue = "DefaultValue" };
                fields.Add("DocumentSecurity", documentSecurity);
                metadata.Fields = fields;
                mockDataRepository
                    .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                    .ReturnsAsync(versions);
                mockDataRepository
                  .Setup(s => s.InsertAsync(It.IsAny<IObjectInstance>()))
                  .ReturnsAsync(versionDetailId);
                mockObjectMetadata
                    .Setup(s => s.GetAsync(It.IsAny<string>()))
                    .ReturnsAsync(metadata);
                //Act
                IObjectInstance result = await documentVersionRepository.CreateVersionDetail(It.IsAny<string>(),
                                    templateId, It.IsAny<string>(), It.IsAny<DocumentVersionType>(), It.IsAny<SelectOption>());

                //Assert
                mockDataRepository
                    .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Exactly(2));
                mockDataRepository
                  .Verify(s => s.InsertAsync(It.IsAny<IObjectInstance>()), Times.Once);
                mockObjectMetadata
                  .Verify(s => s.GetAsync(It.IsAny<string>()), Times.Once);
                Assert.AreEqual("2", result.GetValue<string>("VersionMajor"));
            }
            #region
            private static List<IObjectInstance> CreateVersions() {
                List<IObjectInstance> versions = new List<IObjectInstance>();
                IObjectInstance docVersion = new MockObjectInstance();
                docVersion.SetValue("NumberOfVersions", decimal.One);
                docVersion.SetValue("Id", Guid.NewGuid().ToString());
                docVersion.SetValue("VersionMajor", decimal.One);
                docVersion.SetValue("VersionMinor", decimal.One);
                docVersion.SetValue("VersionRevision", decimal.One);
                versions.Add(docVersion);
                return versions;
            }
            #endregion
        }

        [TestClass]
        public class GetDocumentVersionDetailsTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Return_Document_Version_Detail_With_Valid_Version_Ids() {
                //Arrange
                string[] versionIds = new string[1];
                versionIds[0] = Guid.NewGuid().ToString();
                List<IObjectInstance> documentVersionDetail = new List<IObjectInstance>();
                mockDataRepository
                    .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                    .ReturnsAsync(documentVersionDetail);

                //Act
                List<IObjectInstance> result = await documentVersionRepository.GetDocumentVersionDetails(versionIds);

                //Assert
                mockDataRepository
                    .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Once);
                Assert.AreEqual(documentVersionDetail.Count, result.Count);
            }

        }

        [TestClass]
        public class GetDocumentVersionDetailsByParentIdTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Return_Document_Version_Details_With_Valid_Parent_Id() {
                //Arrange
                List<string> fields = new List<string>();
                fields.Add("Title");
                List<IObjectInstance> versionDetails = new List<IObjectInstance>();

                mockDataRepository
                    .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                    .ReturnsAsync(versionDetails);

                //Act
                List<IObjectInstance> result = await documentVersionRepository.GetDocumentVersionDetailsByParentId(
                                    It.IsAny<string[]>(), fields, true);


                //Assert
                mockDataRepository
                     .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Once);
                Assert.AreEqual(versionDetails.Count, result.Count);
            }


        }

        [TestClass]
        public class MarkDocumentAsExecutedTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Update_Document_As_Executed_With_Valid_Annotaion_Ids() {
                //Arrange
                List<IObjectInstance> documentVersionDetail = new List<IObjectInstance>();
                documentVersionDetail.Add(new MockObjectInstance());
                List<IObjectInstance> listAnnotations = new List<IObjectInstance>();
                IObjectInstance annotation = new MockObjectInstance();
                annotation.SetValue("ContextObject.Id", Guid.NewGuid().ToString());
                listAnnotations.Add(annotation);
                string[] annotationIds = new string[1];
                mockAttachmentRepository
                    .Setup(s => s.GetDocumentsById(It.IsAny<List<string>>(), It.IsAny<List<string>>()))
                    .ReturnsAsync(listAnnotations);
                mockDataRepository
                    .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                    .ReturnsAsync(documentVersionDetail);

                //Act
                await documentVersionRepository.MarkDocumentAsExecuted(annotationIds);

                //Assert
                mockAttachmentRepository
                    .Verify(s => s.GetDocumentsById(It.IsAny<List<string>>(), It.IsAny<List<string>>()), Times.Once);
                mockDataRepository
                    .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Once);
                mockDataRepository
                    .Verify(s => s.UpdateAsync(It.IsAny<IObjectInstance>()), Times.Once);

            }
        }

        [TestClass]
        public class GetVersionForTemplateTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Return_Version_With_Valid_Template_Id() {
                //Arrange
                List<string> fields = new List<string>();
                fields.Add("Title");
                List<IObjectInstance> versionDetails = new List<IObjectInstance>();
                versionDetails.Add(new MockObjectInstance());
                mockDataRepository
                    .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                    .ReturnsAsync(versionDetails);

                //Act
                List<IObjectInstance> result = await documentVersionRepository.GetVersionForTemplate(It.IsAny<string>(), fields);

                //Assert
                mockDataRepository
                     .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Once);
                Assert.AreEqual(versionDetails.Count, result.Count);
            }
        }

        [TestClass]
        public class GetDocumentVersionsByIDsTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Return_Document_Version_With_Valid_Version_Ids() {
                //Arrange
                List<string> fields = new List<string>();
                fields.Add("Title");
                List<IObjectInstance> versionDetails = new List<IObjectInstance>();
                versionDetails.Add(new MockObjectInstance());
                mockDataRepository
                    .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                    .ReturnsAsync(versionDetails);


                //Act
                List<IObjectInstance> result = await documentVersionRepository.GetDocumentVersionsByIDs(It.IsAny<List<string>>(), fields);

                //Assert
                mockDataRepository
                      .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Once);
                Assert.AreEqual(versionDetails.Count, result.Count);
            }
        }

        [TestClass]
        public class UpdateDocumentTypeTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Update_Document_Type_With_Valid_Type() {

                //Arrange
                List<IObjectInstance> documentVersionDetail = new List<IObjectInstance>();
                IObjectInstance item = new MockObjectInstance();
                item.SetValue("DocumentVersionId.Id", Guid.NewGuid());
                documentVersionDetail.Add(item);
                mockDataRepository
                    .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                    .ReturnsAsync(documentVersionDetail);

                //Act
                await documentVersionRepository.UpdateDocumentType(It.IsAny<string>(), It.IsAny<SelectOption>());

                //Assert mockDataRepository
                mockDataRepository
                    .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Once);
                mockDataRepository
                        .Verify(s => s.UpdateAsync(It.IsAny<IObjectInstance>()), Times.Once);
            }
        }

        [TestClass]
        public class UpdateDocumentSecurityAsyncTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Update_Document_Security() {
                //Arrange
                List<IObjectInstance> documentVersionDetail = new List<IObjectInstance>();
                IObjectInstance item = new MockObjectInstance();
                documentVersionDetail.Add(item);
                mockDataRepository
                   .Setup(s => s.GetRecordsAsync(It.IsAny<Query>()))
                   .ReturnsAsync(documentVersionDetail);

                //Act
                await documentVersionRepository.UpdateDocumentSecurityAsync(It.IsAny<string>(), It.IsAny<SelectOption>());

                //Assert
                mockDataRepository
                   .Verify(s => s.GetRecordsAsync(It.IsAny<Query>()), Times.Once);
                mockDataRepository
                        .Verify(s => s.UpdateAsync(It.IsAny<IObjectInstance>()), Times.Once);
            }
        }

        [TestClass]
        public class DeleteDocumentVersionDetailsTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Delete_Document_Version_Details() {
                //Act
                await documentVersionRepository.DeleteDocumentVersionDetails(It.IsAny<List<string>>());

                //Assert
                mockDataRepository
                    .Verify(s => s.UpdateByCriteriaAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<Expression>()), Times.Once);
                mockDataRepository
                    .Verify(s => s.DeleteBulkAsync(It.IsAny<string>(), It.IsAny<List<string>>()), Times.Once);

            }
        }

        [TestClass]
        public class DeleteDocumentVersionsTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Delete_Document_Versions() {

                //Act
                await documentVersionRepository.DeleteDocumentVersions(It.IsAny<List<string>>());

                //Assert
                mockDataRepository
                        .Verify(s => s.DeleteBulkAsync(It.IsAny<string>(), It.IsAny<List<string>>()), Times.Once);
            }
        }

        [TestClass]
        public class UpdateDocumentVersionTests : DocumentVersionRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Update_Document_Version() {
                //Arrange
                Dictionary<string, object> fields = new Dictionary<string, object>();

                //Act
                await documentVersionRepository.UpdateDocumentVersion(fields);

                //Assert
                mockDataRepository
                        .Verify(s => s.UpdateAsync(It.IsAny<IObjectInstance>()), Times.Once);
            }
        }
    }
}
