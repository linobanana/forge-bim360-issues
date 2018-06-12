﻿/////////////////////////////////////////////////////////////////////
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

using Autodesk.Forge;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace forgesample.Controllers
{
  public class BIM360Controller : ApiController
  {
    private const string BASE_URL = "https://developer.api.autodesk.com";

    [HttpGet]
    [Route("api/forge/bim360/container")]
    public async Task<JObject> GetPublicTokenAsync([FromUri]string href)
    {
      string[] idParams = href.Split('/');
      string projectId = idParams[idParams.Length - 1];
      string hubId = idParams[idParams.Length - 3];

      Credentials credentials = await Credentials.FromSessionAsync();

      ProjectsApi projectsApi = new ProjectsApi();
      projectsApi.Configuration.AccessToken = credentials.TokenInternal;
      var project = await projectsApi.GetProjectAsync(hubId, projectId);
      var issues = project.data.relationships.issues.data;
      if (issues.type != "issueContainerId") return null;
      JObject res = new JObject();
      res.Add("container", JObject.Parse(issues.ToString()));
      res.Add("project", JObject.Parse(project.data.ToString()));
      return res;
    }

    public async Task<IRestResponse> Get(string containerId, string resource, string urn)
    {
      Credentials credentials = await Credentials.FromSessionAsync();
      urn = Encoding.UTF8.GetString(Convert.FromBase64String(urn));

      RestClient client = new RestClient(BASE_URL);
      RestRequest request = new RestRequest("/issues/v1/containers/{container_id}/{resource}?filter[target_urn]={urn}", RestSharp.Method.GET);
      request.AddParameter("container_id", containerId, ParameterType.UrlSegment);
      request.AddParameter("urn", urn, ParameterType.UrlSegment);
      request.AddParameter("resource", resource, ParameterType.UrlSegment);
      request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);
      return await client.ExecuteTaskAsync(request);
    }

    public async Task<IRestResponse> GetResource(string urn)
    {
      Credentials credentials = await Credentials.FromSessionAsync();

      string objectName = urn.Substring(urn.LastIndexOf('/') + 1);
      string bucketKey = urn.Substring(urn.LastIndexOf(':') + 1, urn.LastIndexOf('/') - urn.LastIndexOf(':') -1);

      RestClient client = new RestClient(BASE_URL);
      RestRequest request = new RestRequest("/oss/v2/buckets/{bucketKey}/objects/{objectName}", RestSharp.Method.GET);
      request.AddParameter("bucketKey", bucketKey, ParameterType.UrlSegment);
      request.AddParameter("objectName", objectName, ParameterType.UrlSegment);
      request.AddHeader("Authorization", "Bearer " + credentials.TokenInternal);
      request.AddHeader("x-ads-acm-namespace", "WIPDM");
      return await client.ExecuteTaskAsync(request);
    }

    [HttpGet]
    [Route("api/forge/bim360/container/{containerId}/markups/{urn}")]
    public async Task<JArray> DocumentMarkups(string containerId, string urn)
    {
      IRestResponse markupsResponse = await Get(containerId, "markups", urn);

      dynamic markups = JObject.Parse(markupsResponse.Content);
      foreach (dynamic markup in markups.data)
      {
        IRestResponse svg = await GetResource(markup.attributes.resource_urns.svg.ToString());
        IRestResponse screencapture = await GetResource(markup.attributes.resource_urns.screencapture.ToString());

        // replace on the response...
        markup.attributes.resource_urns.svg = svg.Content;
        markup.attributes.resource_urns.screencapture = screencapture.Content;

        markup.links = null;
        markup.relationships = null;
      }

      return markups.data;
    }

    [HttpGet]
    [Route("api/forge/bim360/container/{containerId}/issues/{urn}")]
    public async Task<JArray> DocumentIssues(string containerId, string urn)
    {
      IRestResponse documentIssuesResponse = await Get(containerId, "issues", urn);

      dynamic issues = JObject.Parse(documentIssuesResponse.Content);
      foreach (dynamic issue in issues.data)
      {
        // ToDo?
      }

      return issues.data;
    }
  }
}
