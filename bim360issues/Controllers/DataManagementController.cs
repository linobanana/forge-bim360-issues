/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Autodesk.Forge;
using Autodesk.Forge.Model;
using Newtonsoft.Json.Linq;

namespace bim360issues.Controllers
{
    public class DataManagementController : ControllerBase
    {
        /// <summary>
        /// Credentials on this request
        /// </summary>
        private Credentials Credentials { get; set; }

        /// <summary>
        /// GET TreeNode passing the ID
        /// </summary>
        [HttpGet]
        [Route("api/forge/datamanagement")]
        public async Task<IList<jsTreeNode>> GetTreeNodeAsync(string id)
        {
            Credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            if (Credentials == null) { return null; }

            IList<jsTreeNode> nodes = new List<jsTreeNode>();

            if (id == "#") // root
                return await GetHubsAsync();
            else
            {
                string[] idParams = id.Split('/');
                string resource = idParams[idParams.Length - 2];
                switch (resource)
                {
                    case "hubs": // hubs node selected/expanded, show projects
                        return await GetProjectsAsync(id);
                    case "projects": // projects node selected/expanded, show root folder contents
                        return await GetProjectContents(id);
                    case "folders": // folders node selected/expanded, show folder contents
                        return await GetFolderContents(id);
                    case "items":
                        return await GetItemVersions(id);
                }
            }

            return nodes;
        }

        private async Task<IList<jsTreeNode>> GetHubsAsync()
        {
            IList<jsTreeNode> nodes = new List<jsTreeNode>();

            // the API SDK
            HubsApi hubsApi = new HubsApi();
            hubsApi.Configuration.AccessToken = Credentials.TokenInternal;

            var hubs = await hubsApi.GetHubsAsync();
            foreach (KeyValuePair<string, dynamic> hubInfo in new DynamicDictionaryItems(hubs.data))
            {
                // check the type of the hub to show an icon
                string nodeType = "hubs";
                switch ((string)hubInfo.Value.attributes.extension.type)
                {
                    case "hubs:autodesk.core:Hub":
                        nodeType = "hubs";
                        break;
                    case "hubs:autodesk.a360:PersonalHub":
                        nodeType = "personalHub";
                        break;
                    case "hubs:autodesk.bim360:Account":
                        nodeType = "bim360Hubs";
                        break;
                }

                // create a treenode with the values
                jsTreeNode hubNode = new jsTreeNode(hubInfo.Value.links.self.href, hubInfo.Value.attributes.name, nodeType, true);
                nodes.Add(hubNode);
            }

            return nodes;
        }

        private async Task<IList<jsTreeNode>> GetProjectsAsync(string href)
        {
            IList<jsTreeNode> nodes = new List<jsTreeNode>();

            // the API SDK
            ProjectsApi projectsApi = new ProjectsApi();
            projectsApi.Configuration.AccessToken = Credentials.TokenInternal;

            // extract the hubId from the href
            string[] idParams = href.Split('/');
            string hubId = idParams[idParams.Length - 1];

            var projects = await projectsApi.GetHubProjectsAsync(hubId);
            foreach (KeyValuePair<string, dynamic> projectInfo in new DynamicDictionaryItems(projects.data))
            {
                // check the type of the project to show an icon
                string nodeType = "projects";
                switch ((string)projectInfo.Value.attributes.extension.type)
                {
                    case "projects:autodesk.core:Project":
                        nodeType = "a360projects";
                        break;
                    case "projects:autodesk.bim360:Project":
                        nodeType = "bim360projects";
                        break;
                }

                // create a treenode with the values
                jsTreeNode projectNode = new jsTreeNode(projectInfo.Value.links.self.href, projectInfo.Value.attributes.name, nodeType, true);
                nodes.Add(projectNode);
            }

            return nodes;
        }

        private async Task<IList<jsTreeNode>> GetProjectContents(string href)
        {
            IList<jsTreeNode> nodes = new List<jsTreeNode>();

            // the API SDK
            ProjectsApi projectApi = new ProjectsApi();
            projectApi.Configuration.AccessToken = Credentials.TokenInternal;

            // extract the hubId & projectId from the href
            string[] idParams = href.Split('/');
            string hubId = idParams[idParams.Length - 3];
            string projectId = idParams[idParams.Length - 1];

            var project = await projectApi.GetProjectAsync(hubId, projectId);
            var rootFolderHref = project.data.relationships.rootFolder.meta.link.href;

            return await GetFolderContents(rootFolderHref);
        }

        private async Task<IList<jsTreeNode>> GetFolderContents(string href)
        {
            IList<jsTreeNode> nodes = new List<jsTreeNode>();

            // the API SDK
            FoldersApi folderApi = new FoldersApi();
            folderApi.Configuration.AccessToken = Credentials.TokenInternal;

            // extract the projectId & folderId from the href
            string[] idParams = href.Split('/');
            string folderId = idParams[idParams.Length - 1];
            string projectId = idParams[idParams.Length - 3];

            // check if folder specifies visible types
            JArray visibleTypes = null;
            dynamic folder = (await folderApi.GetFolderAsync(projectId, folderId)).ToJson();
            if (folder.data.attributes != null && folder.data.attributes.extension != null && folder.data.attributes.extension.data != null && !(folder.data.attributes.extension.data is JArray) && folder.data.attributes.extension.data.visibleTypes != null)
                visibleTypes = folder.data.attributes.extension.data.visibleTypes;

            var folderContents = await folderApi.GetFolderContentsAsync(projectId, folderId);
            var folderData = new DynamicDictionaryItems(folderContents.data);
            var folderIncluded = (folderContents.Dictionary.ContainsKey("included") ? new DynamicDictionaryItems(folderContents.included) : null);
            foreach (KeyValuePair<string, dynamic> folderContentItem in folderData)
            {
                // do we need to skipsome item?
                string extension = folderContentItem.Value.attributes.extension.type;
                if (extension.IndexOf("Folder") == -1 && visibleTypes != null && !visibleTypes.ToString().Contains(extension)) continue;


                jsTreeNode itemNode = null;
                if (extension.IndexOf("Document") > 0)
                {
                    foreach (KeyValuePair<string, dynamic> includedItem in folderIncluded)
                        if (includedItem.Value.relationships.item.data.id.IndexOf(folderContentItem.Value.id) != -1)
                            foreach (KeyValuePair<string, dynamic> folderContentItem1 in folderData)
                            {
                                if (folderContentItem1.Value.attributes.extension.type.IndexOf("File") == -1) continue;
                                if (folderContentItem1.Value.attributes.extension.data.sourceFileName == includedItem.Value.attributes.extension.data.sourceFileName)
                                {
                                    string urn = string.Format("{0}|{1}|{2}",
                                        folderContentItem.Value.id, // item urn
                                        Base64Encode(folderContentItem1.Value.relationships.tip.data.id), // version urn
                                        includedItem.Value.attributes.extension.data.viewableId // viewableID
                                    );
                                    itemNode = new jsTreeNode(urn, includedItem.Value.attributes.name, "versions", false);
                                }
                            }
                }
                else
                    itemNode = new jsTreeNode(folderContentItem.Value.links.self.href, folderContentItem.Value.attributes.displayName, (string)folderContentItem.Value.type, true);

                nodes.Add(itemNode);
            }

            return nodes;
        }

        private string GetName(DynamicDictionaryItems folderIncluded, KeyValuePair<string, dynamic> folderContentItem)
        {


            return "N/A";
        }

        private async Task<IList<jsTreeNode>> GetItemVersions(string href)
        {
            IList<jsTreeNode> nodes = new List<jsTreeNode>();

            // the API SDK
            ItemsApi itemApi = new ItemsApi();
            itemApi.Configuration.AccessToken = Credentials.TokenInternal;

            // extract the projectId & itemId from the href
            string[] idParams = href.Split('/');
            string itemId = idParams[idParams.Length - 1];
            string projectId = idParams[idParams.Length - 3];

            var versions = await itemApi.GetItemVersionsAsync(projectId, itemId);
            foreach (KeyValuePair<string, dynamic> version in new DynamicDictionaryItems(versions.data))
            {
                DateTime versionDate = version.Value.attributes.lastModifiedTime;
                string verNum = version.Value.id.Split("=")[1];
                string userName = version.Value.attributes.lastModifiedUserName;

                string urn = string.Empty;
                try { urn = (string)version.Value.relationships.derivatives.data.id; }
                catch { urn = Base64Encode(version.Value.id); } // some BIM 360 versions don't have viewable

                jsTreeNode node = new jsTreeNode(
                    urn,
                    string.Format("v{0}: {1} by {2}", verNum, versionDate.ToString("dd/MM/yy HH:mm:ss"), userName),
                    "versions",
                    false);
                nodes.Add(node);
            }

            return nodes;
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes).Replace("/", "_");
        }

        public class jsTreeNode
        {
            public jsTreeNode(string id, string text, string type, bool children)
            {
                this.id = id;
                this.text = text;
                this.type = type;
                this.children = children;
            }

            public string id { get; set; }
            public string text { get; set; }
            public string type { get; set; }
            public bool children { get; set; }
        }

    }
}
