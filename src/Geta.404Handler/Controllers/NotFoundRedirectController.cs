﻿// Copyright (c) Geta Digital. All rights reserved.
// Licensed under Apache-2.0. See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Web;
using System.Web.Mvc;
using System.Xml;
using BVNetwork.NotFound.Core;
using BVNetwork.NotFound.Core.CustomRedirects;
using BVNetwork.NotFound.Core.Data;
using BVNetwork.NotFound.Models;
using EPiServer.Logging;
using EPiServer.Framework.Localization;
using EPiServer.Shell.Gadgets;

namespace BVNetwork.NotFound.Controllers
{
    [EPiServer.Shell.Web.ScriptResource("ClientResources/Scripts/jquery.blockUI.js")]
    [Gadget(ResourceType = typeof(NotFoundRedirectController),
           NameResourceKey = "GadgetName", DescriptionResourceKey = "GadgetDescription")]
    [EPiServer.Shell.Web.CssResource("ClientResources/Content/RedirectGadget.css")]
    [EPiServer.Shell.Web.ScriptResource("ClientResources/Scripts/jquery.form.js")]
    [Authorize]
    public class NotFoundRedirectController : Controller
    {
        private readonly IRedirectsService _redirectsService;
        private readonly DdsRedirectRepository _ddsRedirectRepository;
        private static readonly ILogger Logger = LogManager.GetLogger();

        public NotFoundRedirectController(
            IRedirectsService redirectsService,
            DdsRedirectRepository ddsRedirectRepository)
        {
            _redirectsService = redirectsService ?? throw new ArgumentNullException(nameof(redirectsService));
            _ddsRedirectRepository = ddsRedirectRepository ?? throw new ArgumentNullException(nameof(ddsRedirectRepository));
        }

        private void CheckAccess()
        {
            if (!EPiServer.Security.PrincipalInfo.HasEditAccess)
            {
                throw new SecurityException("Access denied");
            }
        }

        public ActionResult Index(int? pageNumber, string searchWord, int? pageSize, bool? isSuggestions, bool? showRedirects)
        {
            CheckAccess();

            CustomRedirectHandler.ReloadCustomRedirectHandler();
            if (!string.IsNullOrEmpty(CustomRedirectHandler.CustomRedirectHandlerException))
            {
                return Content("An error has occurred in the dynamic data store" + CustomRedirectHandler.CustomRedirectHandlerException);
            }

            var suggestion = false;
            List<CustomRedirect> customRedirectList;

            if (isSuggestions.HasValue && isSuggestions.Value)
            {
                customRedirectList = GetSuggestions(searchWord);

                suggestion = true;
                var viewData = GetRedirectIndexViewData(pageNumber, customRedirectList, GetSearchResultInfo(searchWord, customRedirectList.Count, suggestion), searchWord, pageSize, suggestion, showRedirects);
                if (customRedirectList.Count > 0)
                {
                    viewData.HighestSuggestionValue = customRedirectList.First().NotfoundErrorCount;
                    viewData.LowestSuggestionValue = customRedirectList.Last().NotfoundErrorCount;
                }
                return View("Index", viewData);
            }

            if (!(showRedirects.HasValue && showRedirects.Value))
            {
                customRedirectList = new List<CustomRedirect>();
                return View("Index", GetRedirectIndexViewData(pageNumber, customRedirectList, LocalizationService.Current.GetString("/gadget/redirects/inactiveredirects"), searchWord, pageSize, suggestion, showRedirects));
            }

            customRedirectList = GetData(searchWord);
            return View("Index", GetRedirectIndexViewData(pageNumber, customRedirectList, GetSearchResultInfo(searchWord, customRedirectList.Count, suggestion), searchWord, pageSize, suggestion, showRedirects));
        }

        public ActionResult SaveSuggestion(string oldUrl, string newUrl, string skipWildCardAppend, int? pageNumber, int? pageSize)
        {
            CheckAccess();
            SaveRedirect(oldUrl, newUrl, skipWildCardAppend, RedirectType.Permanent);

            // delete rows from DB
            var dbAccess = DataAccessBaseEx.GetWorker();
            dbAccess.DeleteRowsForRequest(oldUrl);

            //
            List<CustomRedirect> customRedirectList = GetSuggestions(null);
            string actionInfo = string.Format(LocalizationService.Current.GetString("/gadget/redirects/saveredirect"), oldUrl, newUrl);
            CustomRedirectHandler.ClearCache();
            var viewData = GetRedirectIndexViewData(pageNumber, customRedirectList, actionInfo, null, pageSize, true, true);
            if (customRedirectList.Count > 0)
            {
                viewData.HighestSuggestionValue = customRedirectList.First().NotfoundErrorCount;
                viewData.LowestSuggestionValue = customRedirectList.Last().NotfoundErrorCount;
            }
            return View("Index", viewData);
        }

        public ActionResult Suggestions()
        {
            CheckAccess();
            return Index(null, "", null, true, true);
        }

        [GadgetAction(Text = "Administer")]
        public ActionResult Administer()
        {
            CheckAccess();
            return View();
        }

        [ValidateInput(false)]
        public ActionResult Save(string oldUrl, string newUrl, string skipWildCardAppend, RedirectType redirectType, int? pageNumber, int? pageSize)
        {
            CheckAccess();
            SaveRedirect(oldUrl, newUrl, skipWildCardAppend, redirectType);
            List<CustomRedirect> redirectList = GetData(null);
            string actionInfo = string.Format(LocalizationService.Current.GetString("/gadget/redirects/saveredirect"), oldUrl, newUrl);
            return View("Index", GetRedirectIndexViewData(pageNumber, redirectList, actionInfo, null, pageSize, false, true));

        }

        public void SaveRedirect(string oldUrl, string newUrl, string skipWildCardAppend, RedirectType redirectType)
        {

            Logger.Debug("Adding redirect: '{0}' -> '{1}' ({3})", oldUrl, newUrl, redirectType);
            // Get hold of the datastore
            _redirectsService.AddOrUpdate(new CustomRedirect(oldUrl.Trim(), newUrl.Trim(), skipWildCardAppend != null, redirectType));
            CustomRedirectHandler.ClearCache();

        }

        public ActionResult IgnoreRedirect(string oldUrl, int pageNumber, string searchWord, int pageSize)
        {
            CheckAccess();
            // delete rows from DB
            var dbAccess = DataAccessBaseEx.GetWorker();
            dbAccess.DeleteRowsForRequest(oldUrl);

            // add redirect to dds with state "ignored"
            var redirect = new CustomRedirect
            {
                OldUrl = oldUrl,
                NewUrl = string.Empty,
                State = Convert.ToInt32(RedirectState.Ignored)
            };
            _redirectsService.AddOrUpdate(redirect);
            CustomRedirectHandler.ClearCache();

            List<CustomRedirect> customRedirectList = GetSuggestions(searchWord);
            string actionInfo = string.Format(LocalizationService.Current.GetString("/gadget/redirects/ignoreredirect"), oldUrl);
            RedirectIndexViewData viewData = GetRedirectIndexViewData(pageNumber, customRedirectList, actionInfo, searchWord, pageSize, true, true);
            if (customRedirectList.Count > 0)
            {
                viewData.HighestSuggestionValue = customRedirectList.First().NotfoundErrorCount;
                viewData.LowestSuggestionValue = customRedirectList.Last().NotfoundErrorCount;
            }
            return View("Index", viewData);
        }

        [ValidateInput(false)]
        public ActionResult Delete(string oldUrl, int? pageNumber, string searchWord, int? pageSize)
        {
            CheckAccess();

            Logger.Debug("Deleting redirect: '{0}'", oldUrl);

            _redirectsService.DeleteByOldUrl(oldUrl);
            CustomRedirectHandler.ClearCache();
            List<CustomRedirect> customRedirectList = GetData(searchWord);
            //Make sure that the searchinfo is contained after an item has been deleted - if there is any.
            return View("Index", GetRedirectIndexViewData(pageNumber, customRedirectList, GetSearchResultInfo(searchWord, customRedirectList.Count, false), searchWord, pageSize, false, true));
        }

        /// <summary>
        /// Get the data that will be presented in the view(s).
        /// </summary>
        /// <param name="pageNumber">The current page number for the pager view</param>
        /// <param name="redirectList">The List of redirects</param>
        /// <param name="actionInformation">Text that will be presented in the view</param>
        /// <returns></returns>
        public RedirectIndexViewData GetRedirectIndexViewData(int? pageNumber, List<CustomRedirect> redirectList, string actionInformation, string searchWord, int? pageSize, bool isSuggestions, bool? showRedirects)
        {
            RedirectIndexViewData indexData = new RedirectIndexViewData();
            indexData.IsSuggestions = isSuggestions;
            indexData.ActionInformation = actionInformation;
            indexData.ShowRedirects = showRedirects.HasValue ? showRedirects.Value : false;
            indexData.SearchWord = searchWord;
            indexData.TotalItemsCount = redirectList.Count;
            indexData.PageNumber = pageNumber ?? 1;
            //TODO: read pagersize and pagesize from configuration.
            indexData.PagerSize = 4;
            indexData.PageSize = pageSize ?? 30;
            if (redirectList.Count > indexData.PageSize)
                indexData.CustomRedirectList = redirectList.GetRange(indexData.MinIndexOfItem - 1, indexData.MaxIndexOfItem - indexData.MinIndexOfItem + 1);
            else
                indexData.CustomRedirectList = redirectList;
            return indexData;

        }

        public ActionResult Ignored()
        {
            CheckAccess();
            var ignoredRedirects = _redirectsService.GetIgnored().ToList();
            return View("Ignored", ignoredRedirects);
        }

        public ActionResult Deleted()
        {
            CheckAccess();
            var deletedRedirects = _redirectsService.GetDeleted().ToList();
            return View("Deleted", deletedRedirects);
        }

        public ActionResult Unignore(string url)
        {
            CheckAccess();
            _redirectsService.DeleteByOldUrl(url);
            return Ignored();
        }

        public ActionResult Referers(string url)
        {
            CheckAccess();
            var referers = DataHandler.GetReferers(url);
            ViewData.Add("refererUrl", url);
            return View("Referers", referers);
        }

        public ActionResult DeleteAllIgnored()
        {
            CheckAccess();
            var deleteCount = _redirectsService.DeleteAllIgnored();
            string infoText = string.Format(LocalizationService.Current.GetString("/gadget/redirects/ignoredremoved"), deleteCount);
            ViewData["information"] = infoText;
            return View("Administer");
        }

        public ActionResult DeleteAllSuggestions()
        {
            CheckAccess();
            DataAccessBaseEx.GetWorker().DeleteAllSuggestions();
            ViewData["information"] = LocalizationService.Current.GetString("/gadget/redirects/suggestionsdeleted");
            return View("Administer");
        }

        public ActionResult DeleteAllRedirects()
        {
            CheckAccess();
            _redirectsService.DeleteAll();
            CustomRedirectHandler.ClearCache();
            ViewData["information"] = LocalizationService.Current.GetString("/gadget/redirects/redirectsdeleted");
            return View("Administer");
        }

        /// <summary>
        /// Removed Deleted (410) redirect
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public ActionResult DeleteDeleted(string url)
        {
            CheckAccess();
            _redirectsService.DeleteByOldUrl(url);
            return Deleted();
        }

        [AcceptVerbs(HttpVerbs.Post)]
        public FileUploadJsonResult ImportRedirects(HttpPostedFileBase xmlfile)
        {
            CheckAccess();

            if (!IsXml(xmlfile))
            {
                return new FileUploadJsonResult
                {
                    Data = new { message = LocalizationService.Current.GetString("/gadget/redirects/invalidxmlfile") }
                };
            }

            // Read all redirects from xml file
            RedirectsXmlParser parser = new RedirectsXmlParser(xmlfile.InputStream);
            // Save all redirects from xml file
            CustomRedirectCollection redirects = parser.Load();
            string message;
            if (redirects != null || redirects.Count() != 0)
            {
                _redirectsService.AddOrUpdate(redirects);
                CustomRedirectHandler.ClearCache();
                message = string.Format(LocalizationService.Current.GetString("/gadget/redirects/importsuccess"), redirects.Count());
            }
            else
            {
                message = LocalizationService.Current.GetString("/gadget/redirects/importnone");
            }
            return new FileUploadJsonResult { Data = new { message = message } };
        }

        public XmlActionResult ExportAllRedirects()
        {
            CheckAccess();

            var redirects = _redirectsService.GetSaved().ToList();

            XmlDocument document = new RedirectsXmlParser().Export(redirects);

            return new XmlActionResult(document);
        }

        [AcceptVerbs(HttpVerbs.Post)]
        public FileUploadJsonResult ImportDeleted(HttpPostedFileBase txtFile)
        {
            CheckAccess();

            if (!IsTxt(txtFile))
            {
                return new FileUploadJsonResult
                {
                    Data = new { message = LocalizationService.Current.GetString("/gadget/redirects/invalidtxtfile") }
                };
            }

            var redirects = new CustomRedirectCollection();
            using (var streamReader = new StreamReader(txtFile.InputStream))
            {
                while (streamReader.Peek() >= 0)
                {
                    var url = streamReader.ReadLine();
                    if (!string.IsNullOrEmpty(url))
                    {
                        redirects.Add(new CustomRedirect
                        {
                            OldUrl = url,
                            NewUrl = string.Empty,
                            State = (int)RedirectState.Deleted
                        });
                    }
                }
            }
            string message;
            if (redirects.Count() != 0)
            {
                _redirectsService.AddOrUpdate(redirects);
                CustomRedirectHandler.ClearCache();
                message = string.Format(LocalizationService.Current.GetString("/gadget/redirects/importdeletedsuccess"), redirects.Count());
            }
            else
            {
                message = LocalizationService.Current.GetString("/gadget/redirects/importnone");
            }
            return new FileUploadJsonResult { Data = new { message = message } };
        }

        private bool IsXml(HttpPostedFileBase file)
        {
            return FileIsOfType(file, new[] {"text/xml", "application/xml"}, new[] {"xml"});
        }

        private bool IsTxt(HttpPostedFileBase file)
        {
            return FileIsOfType(file, new[] {"text/plain"}, new[] {"txt"});
        }

        private bool FileIsOfType(HttpPostedFileBase file, string[] allowedContentTypes, string[] allowedExtensions)
        {
            var isAllowedContentType = allowedContentTypes.Any(
                x => file.ContentType.Equals(x, StringComparison.InvariantCultureIgnoreCase));
            if (isAllowedContentType)
            {
                return true;
            }

            return allowedExtensions.Any(x => file.FileName.EndsWith(x, StringComparison.OrdinalIgnoreCase));
        }

        public ActionResult DeleteSuggestions(int maxErrors, int minimumDays)
        {
            CheckAccess();
            DataAccessBaseEx.GetWorker().DeleteSuggestions(maxErrors, minimumDays);
            ViewData["information"] = LocalizationService.Current.GetString("/gadget/redirects/suggestionsdeleted");
            return View("Administer");
        }

        /// <summary>
        /// Get the tekst that will be displayed in the info area of the gadget.
        /// </summary>
        /// <param name="searchWord"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public string GetSearchResultInfo(string searchWord, int count, bool isSuggestions)
        {
            string actionInfo;
            if (string.IsNullOrEmpty(searchWord) && !isSuggestions)
            {
                actionInfo = string.Format(LocalizationService.Current.GetString("/gadget/redirects/storedredirects"), count);
                actionInfo += " " + string.Format(LocalizationService.Current.GetString("/gadget/redirects/andsuggestions"), DataHandler.GetTotalSuggestionCount());
            }
            else if (string.IsNullOrEmpty(searchWord) && isSuggestions)
                actionInfo = string.Format(LocalizationService.Current.GetString("/gadget/redirects/storedsuggestions"), count);
            else if (isSuggestions)
                actionInfo = string.Format(LocalizationService.Current.GetString("/gadget/redirects/searchsuggestions"), searchWord, count);
            else
                actionInfo = string.Format(LocalizationService.Current.GetString("/gadget/redirects/searchresult"), searchWord, count);
            return actionInfo;
        }

        /// <summary>
        /// Get custom redirect data from dynamic data store.
        /// </summary>
        /// <param name="searchWord"></param>
        /// <returns></returns>
        public List<CustomRedirect> GetData(String searchWord)
        {
            List<CustomRedirect> customRedirectList;
            if (string.IsNullOrEmpty(searchWord))
            {
                customRedirectList = _redirectsService.GetSaved().ToList();
            }
            else
                customRedirectList = _redirectsService.Search(searchWord).ToList();

            return customRedirectList;
        }

        public List<CustomRedirect> GetSuggestions(String searchWord)
        {

            var customRedirectList = new List<CustomRedirect>();
            var dict = DataHandler.GetRedirects();

            foreach (KeyValuePair<string, int> redirect in dict)
            {
                customRedirectList.Add(new CustomRedirect(redirect.Key, Convert.ToInt32(RedirectState.Suggestion), redirect.Value));
            }

            return customRedirectList;
        }

        public List<CustomRedirect> GetDeletedUrls() => _redirectsService.GetDeleted().ToList();

        public static string GadgetEditMenuName
        {
            get { return LocalizationService.Current.GetString("/gadget/redirects/configure"); }
        }

        public static string GadgetName
        {
            get { return LocalizationService.Current.GetString("/gadget/redirects/name"); }
        }

        public static string GadgetDescription
        {
            get { return LocalizationService.Current.GetString("/gadget/redirects/description"); }
        }

        public ActionResult AddDeletedUrl(string oldUrl)
        {
            CheckAccess();

            // add redirect to dds with state "deleted"
            var redirect = new CustomRedirect
            {
                OldUrl = oldUrl,
                NewUrl = string.Empty,
                State = Convert.ToInt32(RedirectState.Deleted)
            };
            _redirectsService.AddOrUpdate(redirect);
            CustomRedirectHandler.ClearCache();

            // delete rows from DB
            var dbAccess = DataAccessBaseEx.GetWorker();
            dbAccess.DeleteRowsForRequest(oldUrl);

            //
            List<CustomRedirect> customRedirectList = GetDeletedUrls();
            CustomRedirectHandler.ClearCache();
            return Deleted();
        }

        [AcceptVerbs(HttpVerbs.Post)]
        public ActionResult MigrateDdsToSql()
        {
            CheckAccess();

            var ddsRequests = _ddsRedirectRepository.GetAll().ToList();
            var count = ddsRequests.Count;
            foreach (var ddsRequest in ddsRequests)
            {
                _redirectsService.AddOrUpdate(new CustomRedirect
                {
                    State = ddsRequest.State,
                    NewUrl = ddsRequest.NewUrl,
                    OldUrl = ddsRequest.OldUrl,
                    WildCardSkipAppend = ddsRequest.WildCardSkipAppend,
                    RedirectType = RedirectType.Permanent
                });

                _ddsRedirectRepository.Delete(ddsRequest);
            }

            ViewData["information"] = $"Migrated {count} redirects from DDS to SQL";

            CustomRedirectHandler.ClearCache();
            return View("Administer");
        }
    }

    public class FileUploadJsonResult : JsonResult
    {

        public override void ExecuteResult(ControllerContext context)
        {
            this.ContentType = "text/html";
            context.HttpContext.Response.Write("<textarea>");
            base.ExecuteResult(context);
            context.HttpContext.Response.Write("</textarea>");
        }
    }
}
