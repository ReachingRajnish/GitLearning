
using Apttus.Contracts.DAL.Implementations;
using Apttus.Contracts.UnitTest.Common;
using Apttus.DataAccess.Common.Interface;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apttus.Contracts.DAL.UnitTest.Implementations.AzureSQL {
    public class AsyncMergeCallRepositoryTestsBase {
        protected Mock<IDataRepository> mockDataRepository;
        protected AsyncMergeCallRepository asyncMergeCallRepository;

        [TestInitialize]
        public void Inttalize() {
            mockDataRepository = new Mock<IDataRepository>();
            asyncMergeCallRepository = new AsyncMergeCallRepository(mockDataRepository.Object);
        }
    }

    public class AsyncMergeCallRepositoryTests {

        [TestClass]
        public class GetMergeCallAsyncTests : AsyncMergeCallRepositoryTestsBase {
            [TestMethod]
            public async Task Should_Return_Merge_Call_With_Valid_Call_Id() {
                //Arrange
                IObjectInstance actual = new MockObjectInstance();
                actual.SetValue("Name", "Test");
                mockDataRepository
                    .Setup(s => s.GetObjectByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<bool>()))
                    .ReturnsAsync(actual);

                //Act
                IObjectInstance result = await asyncMergeCallRepository.GetMergeCallAsync(It.IsAny<string>());

                //Assert
                mockDataRepository
                        .Verify(s => s.GetObjectByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<bool>()), Times.Once);
                Assert.AreEqual(actual.GetValue<string>("Name"), result.GetValue<string>("Name"));
            }
        }
    }
}
