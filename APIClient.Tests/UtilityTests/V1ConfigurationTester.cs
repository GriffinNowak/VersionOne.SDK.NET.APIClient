﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using VersionOne.SDK.APIClient.Obsolete;

namespace VersionOne.SDK.APIClient.Tests.UtilityTests
{
    [TestClass]
    public class V1ConfigurationTester
    {
        private static void RunTest(string testName, bool exepectedTracking, TrackingLevel exepectedStoryLevel, TrackingLevel expectedDefectLevel)
        {
            V1Configuration testSubject = new V1Configuration(new XmlResponseConnector("TestData.xml", "config.v1/" + testName, "V1ConfigurationTester"));
            Assert.AreEqual(exepectedTracking, testSubject.EffortTracking);
            Assert.AreEqual(exepectedStoryLevel, testSubject.StoryTrackingLevel);
            Assert.AreEqual(expectedDefectLevel, testSubject.DefectTrackingLevel);
        }

        [TestMethod]
        public void TrueOffOff()
        {
            RunTest("TrueOffOff", true, TrackingLevel.Off, TrackingLevel.Off);
        }

        [TestMethod]
        public void TrueOnOn()
        {
            RunTest("TrueOnOn", true, TrackingLevel.On, TrackingLevel.On);
        }

        [TestMethod]
        public void TrueOffOn()
        {
            RunTest("TrueOffOn", true, TrackingLevel.Off, TrackingLevel.On);
        }

        [TestMethod]
        public void TrueOnOff()
        {
            RunTest("TrueOnOff", true, TrackingLevel.On, TrackingLevel.Off);
        }

        [TestMethod]
        public void FalseOffOff()
        {
            RunTest("FalseOffOff", false, TrackingLevel.Off, TrackingLevel.Off);
        }

        [TestMethod]
        public void FalseOnOn()
        {
            RunTest("FalseOnOn", false, TrackingLevel.On, TrackingLevel.On);
        }

        [TestMethod]
        public void FalseOffOn()
        {
            RunTest("FalseOffOn", false, TrackingLevel.Off, TrackingLevel.On);
        }

        [TestMethod]
        public void FalseOnOff()
        {
            RunTest("FalseOnOff", false, TrackingLevel.On, TrackingLevel.Off);
        }
    }
}
