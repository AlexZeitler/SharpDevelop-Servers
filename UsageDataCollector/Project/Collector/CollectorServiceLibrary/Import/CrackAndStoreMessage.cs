﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ICSharpCode.UsageDataCollector.Contracts;
using ICSharpCode.UsageDataCollector.DataAccess.Collector;

namespace ICSharpCode.UsageDataCollector.ServiceLibrary.Import
{
    public class CrackAndStoreMessage
    {
        UsageDataMessage message = null;
        CollectorRepository repository = null;

        public CrackAndStoreMessage(UsageDataMessage msg, CollectorRepository repo)
        {
            message = msg;
            repository = repo;
        }

        // we intentionally don't build the full model in memory first (user -> sessions -> data tables)
        // avoiding concurrency issues (eg type tables) is more important than fewer database writes
        public void ProcessMessage()
        {
            string userGuid = message.UserID.ToString();
            if (String.IsNullOrEmpty(userGuid))
            {
                return;
            }

            // Preprocessing of type tables (don't insert any usage data unless type updates went through properly)
            PreProcessEnvironmentDataNames();
            PreProcessActivationMethods();
            PreProcessFeatures();
            // TODO: Exceptions

            User modelUser = repository.FindUserByGuid(userGuid);
            if (null == modelUser)
            {
                modelUser = new User()
                {
                    AssociatedGuid = userGuid
                };

                repository.Context.Users.AddObject(modelUser);
                repository.Context.SaveChanges();
            }

            foreach (UsageDataSession msgSession in message.Sessions)
            {
                Session modelSession = new Session()
                {
                    ClientSessionId = msgSession.SessionID,
                    StartTime = msgSession.StartTime,
                    EndTime = msgSession.EndTime,
                    UserId = modelUser.Id
                };

                repository.Context.Sessions.AddObject(modelSession);
                repository.Context.SaveChanges(); // TODO: temp solution only (causes too many db writes)

                List<EnvironmentDataName> storedEnvNames = repository.GetEnvironmentDataNames().ToList(); // cacheable

                var insertEnvProperties = (from prop in msgSession.EnvironmentProperties
                                            join envName in storedEnvNames on prop.Name equals envName.Name
                                            select new EnvironmentData()
                                            {
                                                SessionId = modelSession.Id,
                                                EnvironmentDataNameId = envName.Id,
                                                EnvironmentDataValue = prop.Value
                                            });

                foreach (var ede in insertEnvProperties)
                    repository.Context.EnvironmentDatas.AddObject(ede);


                List<ActivationMethod> storedActivationMethods = repository.GetActivationMethods().ToList(); // cacheable
                List<Feature> storedFeatures = repository.GetFeatures().ToList(); // cacheable

                var insertFeatureUse = (from fu in msgSession.FeatureUses
                                        join f in storedFeatures on fu.FeatureName equals f.Name
                                        join am in storedActivationMethods on fu.ActivationMethod equals am.Name
                                        select new ICSharpCode.UsageDataCollector.DataAccess.Collector.FeatureUse()
                             {
                                 ActivationMethodId = am.Id,
                                 FeatureId = f.Id,
                                 SessionId = modelSession.Id,
                                 UseTime = fu.Time,
                                 EndTime = fu.EndTime
                             });

                foreach (var fue in insertFeatureUse)
                    repository.Context.FeatureUses.AddObject(fue);

                repository.Context.SaveChanges();
                
            }

        }

        protected void PreProcessEnvironmentDataNames()
        {
            List<string> distinctMsgEnvProperties = (from s in message.Sessions
                                                     from p in s.EnvironmentProperties
                                                     select p.Name).Distinct().ToList();

            // did we receive environment data at all?
            if (distinctMsgEnvProperties.Count > 0)
            {
                List<string> knownDataNames = repository.GetEnvironmentDataNameNames().ToList(); // cacheable
                List<string> missing = distinctMsgEnvProperties.Except(knownDataNames).ToList();

                // this happens rarely for environment data names
                if (missing.Count > 0)
                {
                    foreach (string envdn in missing)
                    {
                        EnvironmentDataName modelEdn = new EnvironmentDataName()
                        {
                            Name = envdn
                        };

                        repository.Context.EnvironmentDataNames.AddObject(modelEdn);
                    }

                    repository.Context.SaveChanges();
                }
            }
        }

        protected void PreProcessActivationMethods()
        {
            List<string> distinctMsgActivationMethods = (from s in message.Sessions
                                                         from fu in s.FeatureUses
                                                         select fu.ActivationMethod).Distinct().ToList();

            if (distinctMsgActivationMethods.Count > 0)
            {
                List<string> knownActivationMethods = repository.GetActivationMethodNames().ToList(); // cacheable
                List<string> missing = distinctMsgActivationMethods.Except(knownActivationMethods).ToList();

                if (missing.Count > 0)
                {
                    foreach (string am in missing)
                    {
                        ActivationMethod modelAM = new ActivationMethod()
                        {
                            Name = am
                        };

                        repository.Context.ActivationMethods.AddObject(modelAM);
                    }

                    repository.Context.SaveChanges();
                }
            }
        }

        protected void PreProcessFeatures()
        {
            List<string> distinctMsgFeatures =  (from s in message.Sessions
                                                 from fu in s.FeatureUses
                                                 select fu.FeatureName).Distinct().ToList();

            if (distinctMsgFeatures.Count > 0)
            {
                List<string> knownFeatures = repository.GetFeatureNames().ToList(); // cacheable
                List<string> missing = distinctMsgFeatures.Except(knownFeatures).ToList();

                if (missing.Count > 0)
                {
                    foreach (string fn in missing)
                    {
                        Feature modelFeature = new Feature()
                        {
                            Name = fn
                        };

                        repository.Context.Features.AddObject(modelFeature);
                    }

                    repository.Context.SaveChanges();
                }
            }
        }
 
    }
}