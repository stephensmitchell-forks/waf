﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Waf.Applications;
using System.Waf.Applications.Services;
using System.Waf.Presentation.Services;
using System.Waf.UnitTesting;
using System.Windows.Controls;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Test.Waf.Presentation.Services.SettingsServiceTest.DoubleNestedTypeTest;

namespace Test.Waf.Presentation.Services
{
    [TestClass]
    public class SettingsServiceTest
    {
        [TestMethod]
        public void GetAndSaveTest()
        {
            var settingsService = new SettingsService();
            AssertNoErrorOccurred(settingsService);
            Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ApplicationInfo.ProductName, "Settings", "Settings.xml"), settingsService.FileName);

            var settingsFileName = Path.Combine(Environment.CurrentDirectory, "Settings1.xml");
            settingsService.FileName = settingsFileName;
            if (File.Exists(settingsFileName)) File.Delete(settingsFileName);

            var testSettings2 = settingsService.Get<TestSettings2>();
            Assert.AreEqual(4.2, testSettings2.Value);
            Assert.IsTrue(testSettings2.IsActive);

            AssertHelper.ExpectedException<InvalidOperationException>(() => settingsService.FileName = Path.Combine(Environment.CurrentDirectory, "Settings2.xml"));

            testSettings2.Value = 3.14;
            testSettings2.IsActive = false;

            Assert.IsFalse(File.Exists(settingsFileName));
            settingsService.Save();
            Assert.IsTrue(File.Exists(settingsFileName));

            var testSettings1 = settingsService.Get<TestSettings1>();
            Assert.AreNotEqual(default(Guid), testSettings1.UserId);
            Assert.AreNotEqual(default(Guid), testSettings1.LastRun);
            testSettings1.LastRun = new DateTime(2000, 1, 1);
            testSettings1.LastOpenedFiles = new[] { "MruFile1", "MruFile2" };
            testSettings1.ReplaceFiles(new[] { "File1" });

            settingsService.Dispose();
            Assert.IsTrue(File.Exists(settingsFileName));

            // Now read just one setting from the file with another instance -> the other setting must stay "untouched" in the file
            settingsService = new SettingsService();
            AssertNoErrorOccurred(settingsService);
            settingsService.FileName = settingsFileName;
            testSettings2 = settingsService.Get<TestSettings2>();
            Assert.AreEqual(3.14, testSettings2.Value);
            testSettings2.Value = 72;
            settingsService.Dispose();

            // Read the saved values with another instance
            settingsService = new SettingsService();
            AssertNoErrorOccurred(settingsService);
            settingsService.FileName = settingsFileName;
            testSettings2 = settingsService.Get<TestSettings2>();
            Assert.AreEqual(72, testSettings2.Value);
            Assert.IsFalse(testSettings2.IsActive);

            testSettings1 = settingsService.Get<TestSettings1>();
            Assert.AreNotEqual(default(Guid), testSettings1.UserId);
            Assert.AreEqual(new DateTime(2000, 1, 1), testSettings1.LastRun);
            AssertHelper.SequenceEqual(new[] { "MruFile1", "MruFile2" }, testSettings1.LastOpenedFiles);
            AssertHelper.SequenceEqual(new[] { "File1" }, testSettings1.FileNames);
            settingsService.Dispose();
        }

        [TestMethod]
        public void CompatibleWithDataContractSerializer()
        {
            var settingsFileName = Path.Combine(Environment.CurrentDirectory, "Settings2.xml");
            if (File.Exists(settingsFileName)) File.Delete(settingsFileName);

            TestSettings1 testSettings1;
            TestSettings2 testSettings2;
            using (var settingsService = new SettingsService())
            {
                AssertNoErrorOccurred(settingsService);
                settingsService.FileName = settingsFileName;
                testSettings1 = settingsService.Get<TestSettings1>();
                testSettings2 = settingsService.Get<TestSettings2>();
            }

            using (var stream = File.OpenRead(settingsFileName))
            {
                var dcs = new DataContractSerializer(typeof(List<object>), new[] { typeof(TestSettings1), typeof(TestSettings2) });
                var settings = (List<object>)dcs.ReadObject(stream);
                Assert.AreEqual(2, settings.Count);
                Assert.AreEqual(testSettings1.UserId, settings.OfType<TestSettings1>().Single().UserId);
                Assert.AreEqual(testSettings2.Value, settings.OfType<TestSettings2>().Single().Value);
            }
        }

        [TestMethod]
        public void ErrorOccurredTest()
        {
            var settingsService = new SettingsService();
            var settingsFileName = Path.Combine(Environment.CurrentDirectory, "Settings3.xml");
            settingsService.FileName = settingsFileName;
            if (File.Exists(settingsFileName)) File.Delete(settingsFileName);
            settingsService.Save();
            Assert.IsFalse(File.Exists(settingsFileName));

            var testSettings1 = settingsService.Get<TestSettings1>();
            settingsService.Save();
            Exception error = null;
            settingsService.ErrorOccurred += (sender, e) => error = e.Error;
            using (var stream = File.OpenRead(settingsFileName))
            {
                settingsService.Dispose();
            }
            Assert.IsInstanceOfType(error, typeof(IOException));

            File.WriteAllText(settingsFileName, "<WrongFormat xmlns=\"http://schemas.datacontract.org/2004/07/Dummy\" xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\"><MyEnum i:nil=\"true\"/></WrongFormat>");
            settingsService = new SettingsService();
            settingsService.ErrorOccurred += (sender, e) => error = e.Error;
            settingsService.FileName = settingsFileName;
            error = null;
            settingsService.Get<TestSettings1>();
            Assert.IsInstanceOfType(error, typeof(XmlException));
            error = null;
            settingsService.Save();
            Assert.IsInstanceOfType(error, typeof(XmlException));
            if (File.Exists(settingsFileName)) File.Delete(settingsFileName);

            // Now it is repaired with default values
            settingsService = new SettingsService();
            settingsService.ErrorOccurred += (sender, e) => error = e.Error;
            settingsService.FileName = settingsFileName;
            error = null;
            settingsService.Get<TestSettings1>();
            Assert.IsNull(error);

            settingsService = new SettingsService();
            settingsService.ErrorOccurred += (sender, e) => error = e.Error;
            settingsService.FileName = settingsFileName;
            settingsService.Get<NotSerializableTest>();
            error = null;
            settingsService.Save();
            Assert.IsInstanceOfType(error, typeof(SerializationException));
        }

        private static void AssertNoErrorOccurred(ISettingsService settingsService)
        {
            settingsService.ErrorOccurred += (sender, e) => Assert.Fail(nameof(settingsService.ErrorOccurred) + " event was raised: " + e.Error);
        }

        public class DoubleNestedTypeTest
        {
            [DataContract, KnownType(typeof(string[]))]
            public class TestSettings1 : UserSettingsBase
            {
                [DataMember(Name = "FileNames")]
                private readonly List<string> fileNames = new List<string>();

                [DataMember]
                public Guid UserId { get; private set; }

                [DataMember]
                public bool? ActivateFeature1 { get; set; }

                [DataMember]
                public DateTime LastRun { get; set; }

                [DataMember]
                public IReadOnlyList<string> LastOpenedFiles { get; set; }

                public IReadOnlyList<string> FileNames => fileNames;

                public void ReplaceFiles(IEnumerable<string> newFileNames)
                {
                    fileNames.Clear();
                    fileNames.AddRange(newFileNames);
                }

                protected override void SetDefaultValues()
                {
                    UserId = Guid.NewGuid();
                    ActivateFeature1 = null;
                    LastRun = DateTime.MinValue.ToUniversalTime();
                    LastOpenedFiles = Array.Empty<string>();
                }
            }
        }

        [DataContract(Namespace = "urn:testNS", Name = "MySettings")]
        public class TestSettings2 : UserSettingsBase
        {
            [DataMember]
            public double Value { get; set; }

            [DataMember]
            public bool IsActive { get; set; }

            protected override void SetDefaultValues()
            {
                Value = 4.2;
                IsActive = true;
            }
        }

        [DataContract]
        public class NotSerializableTest : UserSettingsBase
        {
            [DataMember]
            public Button Button { get; set; }

            protected override void SetDefaultValues()
            {
                Button = new Button();
            }
        }
    }
}
