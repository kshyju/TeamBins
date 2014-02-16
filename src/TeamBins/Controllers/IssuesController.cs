﻿using Microsoft.AspNet.SignalR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using TeamBins;
using TeamBins.DataAccess;
using TeamBins.Helpers.Enums;
using TeamBins.Helpers.Infrastructure;
using TeamBins.Services;
using TechiesWeb.TeamBins.Infrastructure;
using TechiesWeb.TeamBins.ViewModels;

namespace TechiesWeb.TeamBins.Controllers
{   
    public class IssuesController : BaseController
    {
        private IssueService issueService;
        public IssuesController() {
            issueService=new IssueService(new Repositary(),UserID,TeamID);        
        }

        public IssuesController(IRepositary repositary) :base(repositary)
        {
        }

        #region public methods

        public ActionResult Backlog()
        {
            try
            {
                IssueListVM bugListVM = new IssueListVM();
                var projectList = repo.GetProjects(TeamID).Where(s => s.TeamID == TeamID).ToList();
                if (projectList.Count > 0)
                {
                    bugListVM = GetBugList(LocationType.BKLOG.ToString(),TeamID);
                    bugListVM.ProjectsExist = true;
                }
                return View("Index", bugListVM);
            }
            catch (Exception ex)
            {
                // to do : Log error
                return View("Error");
            }
        }
      
        public ActionResult Completed()
        {
            try
            {
                IssueListVM bugListVM = new IssueListVM();
                var projectList = repo.GetProjects(TeamID).Where(s => s.TeamID == TeamID).ToList();
                if (projectList.Count > 0)
                {
                    bugListVM = GetBugList(LocationType.ARCHV.ToString(),TeamID);
                    bugListVM.ProjectsExist = true;
                }
                return View("Index", bugListVM);
            }
            catch (Exception ex)
            {
                // to do : Log error
                return View("Error");
            }
        }


        public ActionResult Index(int size = 50, string iteration = "current")
        {
            try
            {                
                IssueListVM bugListVM = new IssueListVM ();
                var projectList = repo.GetProjects(TeamID).ToList();
                                
                if(projectList.Count==0)
                {
                    return RedirectToAction("Index", "Projects");
                }
                else 
                {
                    List<IssueVM> issueVMs = new List<IssueVM>();

                    if (Request.IsAjaxRequest())
                    {
                        issueVMs = issueService.GetIssueListVMs(iteration, TeamID, size);
                        return Json(issueVMs, JsonRequestBehavior.AllowGet);
                    }
                    else
                    {
                        bugListVM = GetBugList(LocationType.SPRNT.ToString(), TeamID);
                        bugListVM.ProjectsExist = true;
                        return View("Index", bugListVM);
                    }
                }
               
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return View("Error");
            }
        }

        [HttpPost]
        public ActionResult Add(CreateIssue model, List<HttpPostedFileBase> files)
        {
            try
            {
                Issue issuePreviousVersion = new Issue();
                if (ModelState.IsValid)
                {
                    Issue bug = new Issue { ID = model.ID ,  CreatedByID = UserID,  TeamID = TeamID};
                    if (model.ID != 0)
                    {
                        bug = repo.GetIssue(model.ID);                      
                    }
                    else
                    {
                        bug.Description = model.Title;
                    }

                    issuePreviousVersion = ObjectCloner.DeepClone<Issue>(bug);

                    bug.Title = model.Title;
                    bug.Description = model.Description;
                    bug.TeamID = TeamID;
                   
                    LoadDefaultIssueValues(bug, model);

                    var status = repo.GetStatuses().FirstOrDefault(s => s.ID == bug.StatusID);
                    if (status != null && (status.Name.ToUpper() == "CLOSED" || status.Name.ToUpper() == "COMPLETED"))
                    {
                        //Move the location to "Completed"
                        bug.Location = LocationType.ARCHV.ToString();
                    }

                    OperationStatus result = repo.SaveIssue(bug);
                    if (!result.Status)
                    {
                        log.Debug(result);
                        return Json(new { Status = "Error", Message = "Error saving issue" });
                    }
                    else
                    {
                        var issue = repo.GetIssue(result.OperationID);
                        if (issue != null)
                        {
                            var teamActivity = SaveActivity(model, issue,issuePreviousVersion,  status,result.OperationID, model.ID== 0);
                            var context = GlobalHost.ConnectionManager.GetHubContext<IssuesHub>();
                            if (teamActivity != null)
                            {
                                var activityVM = issueService.GetActivityVM(teamActivity);                              
                                context.Clients.Group(TeamID.ToString()).addNewTeamActivity(activityVM);
                            }
                            if (Request.IsAjaxRequest())
                            {
                                var issueVM = issueService.GetIssueVM(issue);
                                context.Clients.Group(TeamID.ToString()).addIssueToIssueList(issueVM);

                                return Json(new { Status = "Success" });
                            }
                        }
                    }
                   

                    if ((files != null) && (files.Count() > 0))
                    {
                        int fileCounter = 0;
                        foreach (var file in files)
                        {
                            fileCounter = SaveAttachedDocument(model, result, fileCounter, file);
                        }
                    }

                    if (result.Status)
                    {
                        if (model.IsFromModalWindow)
                        {
                            if (Request.UrlReferrer != null)
                                return Redirect(Request.UrlReferrer.AbsoluteUri);
                        }
                        return RedirectToAction("Index");
                    }
                }
            }
            catch (MissingSettingsException mex)
            {
                log.Debug(mex);
                return Json(new { Status = "Error", Message = String.Format("You need to set a value for {0} first.", mex.MissingSettingName) });
            }
            catch (Exception ex)
            {
                log.Error(ex);
                if (Request.IsAjaxRequest())
                {
                    return Json(new { Status = "Error", Message = "Error saving issue" });
                }
            }
            LoadDropDownsForCreate(model);
            return View(model);
        }

        [VerifyLogin]
        public ActionResult Edit(int id)
        {
            var bug = repo.GetIssue(id);
            if (bug != null)
            {
                var editVM = new CreateIssue();
                editVM.Title = bug.Title;
                editVM.Description = bug.Description;
                LoadDropDownsForCreate(editVM);

                editVM.SelectedCategory = bug.Category.ID;
                editVM.SelectedPriority = bug.Priority.ID;
                editVM.SelectedProject = bug.Project.ID;
                editVM.SelectedStatus = bug.Status.ID;
                editVM.SelectedIteration = bug.Location;
                editVM.CreatedDate = bug.CreatedDate.ToShortDateString();

                //var allDocuments = repo.GetDocuments(id, "Bug");
                /* var images = allDocuments.Where(x => x.Extension.ToUpper() == ".JPG" || x.Extension.ToUpper() == ".PNG");
                 foreach (var img in images)
                 {
                     var imgVM = new DocumentVM { FileName = img.DocName };
                     imgVM.FileKey = img.DocKey;
                     editVM.Images.Add(imgVM);
                 }*/

                if (Request.IsAjaxRequest())
                {
                    editVM.IsFromModalWindow = true;
                    return PartialView("~/Views/Issues/Partial/Edit.cshtml", editVM);
                }
                return View(editVM);
            }
            return View("NotFound");
        }
               

        public ActionResult Details(int id=0,int commentId=0)
        {
            int issueId = 0;
            try
            {               
                if(id>0)
                {
                    issueId = id;
                }                   
                else if(id==0 && commentId>0)
                {
                    issueId= repo.GetComment(commentId).IssueID;                   
                }

                var bug = repo.GetIssue(issueId);

                IssueDetailVM bugVm = new IssueDetailVM { ID = bug.ID, Title = bug.Title };
                bugVm.Description = (bug.Description == null ? "" : bug.Description);
                bugVm.CreatedDate = bug.CreatedDate.ToString("g");
                bugVm.OpenedBy = bug.CreatedBy.FirstName;
                bugVm.Title = bug.Title;
                bugVm.Project = bug.Project.Name;
                bugVm.Category = bug.Category.Name;
                bugVm.ProjectID = bug.ProjectID;
                bugVm.TeamID = bug.TeamID;
                bugVm.Status = bug.Status.Name;
                bugVm.Priority = bug.Priority.Name;
                bugVm.StatusCode = bug.Status.Name;
                bugVm.Iteration = ProjectService.GetIterationName(bug.Location);
                if (bug.DueDate.HasValue)
                    bugVm.IssueDueDate = (bug.DueDate.Value.Year > 2000 ? bug.DueDate.Value.ToShortDateString() : "");

                var allDocuments = repo.GetDocuments(issueId);
                foreach (var img in allDocuments)
                {
                    var imgVM = new DocumentVM { FileName = img.FileName, FileKey = img.FileAlias };
                    imgVM.FileExtn = Path.GetExtension(img.FileName);

                    if (imgVM.FileExtn.ToUpper() == ".JPG" || imgVM.FileExtn.ToUpper() == ".PNG")
                        bugVm.Images.Add(imgVM);
                    else
                        bugVm.Attachments.Add(imgVM);

                }
                
                //Get Members
                issueService.LoadIssueMembers(issueId, bugVm, UserID);
                issueService.SetUserPermissionsForIssue(bugVm, UserID, TeamID);
                return View(bugVm);
            }
            catch (Exception ex)
            {
                log.Error("error loading issue " + issueId, ex);
                return View("Error");
            }
        }

        [HttpPost]
        [VerifyLogin]
        public ActionResult RemoveMember(int id, int memberId)
        {
            try
            {
                repo.DeleteIssueMember(id, memberId);
                return Json(new { Status = "Success" });
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return Json(new { Status = "Error", Message = "Error deleting issue member" });
            }
        }

        [HttpPost]
        public int SavePreference(bool CreateAndEditMode)
        {
            Session["CreateAndEditMode"] = CreateAndEditMode;
            return 1;
        }

        [HttpPost]
        [VerifyLogin]
        public JsonResult Star(int id, string mode)
        {
            //to do : Check user has permission to do this
            string starClass = "glyphicon-star-empty";
            string starMode = "unstarred";

            try
            {
                if (mode.ToUpper() == "UNSTARRED")
                {
                    issueService.StarIssue(id, UserID);
                    starClass = "glyphicon-star";
                    starMode = "starred";
                }
                else if (mode.ToUpper() == "STARRED")
                {
                    issueService.UnStarIssue(id, UserID);
                }
                return Json(new { Status = "Success", StarClass = starClass, Mode = starMode });
            }
            catch(Exception ex)
            {
                log.Error("Error staring issue "+ id, ex);
                return Json(new { Status = "Error" });
            }
           
        }

        [HttpPost]
        public int AddMember(int memberId, int issueId)
        {
            try
            {
                issueService.SaveIssueMember(issueId, memberId,UserID);
                return 1;
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return 0;
            }
        }

        public ActionResult IssueMembers(int id)
        {
            var vm = new IssueDetailVM { ID = id };
            issueService.LoadIssueMembers(id, vm,UserID);
            issueService.SetUserPermissionsForIssue(vm, UserID, TeamID);
            return PartialView("Partial/Members", vm);
        }

        [HttpPost]
        public ActionResult Comment(NewIssueCommentVM model)
        {
            try
            {
                if (ModelState.IsValid)
                {

                    model.CommentBody = HttpUtility.HtmlEncode(model.CommentBody);
                    var comment = new Comment { CommentText = model.CommentBody, IssueID = model.IssueID, CreatedByID = UserID, CreatedDate = DateTime.Now };
                    var res = repo.SaveComment(comment);

                    var activity = issueService.SaveActivity(comment,TeamID);
                    if (activity != null)
                    {
                        var activityVM = new CommentService(repo, SiteBaseURL).GetActivityVM(activity);

                        var context = GlobalHost.ConnectionManager.GetHubContext<IssuesHub>();                        
                        context.Clients.Group(TeamID.ToString()).addNewTeamActivity(activityVM);

                        var commentVM = new CommentService(repo, SiteBaseURL).GetCommentVM(comment.ID);
                        context.Clients.Group(TeamID.ToString()).addNewComment(commentVM);
                    }
                    return Json(new { Status = "Success", NewCommentID = res.OperationID });
                }
                return Json(new { Status = "Error" });
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return Json(new { Status = "Error" });
            }
        }

        public ActionResult Comment(int id)
        {
            var comment = repo.GetComment(id);
            if (comment != null)
            {
                var commentVM = new CommentVM { ID = comment.ID, AuthorName = comment.Author.FirstName, CommentBody = comment.CommentText, CreativeDate = comment.CreatedDate.ToString("g") };
                commentVM.AvatarHash = UserService.GetImageSource(comment.Author.EmailAddress, 42);
                commentVM.CreatedDateRelative = comment.CreatedDate.ToShortDateString(); //.ToRelativeDateTime();
                return PartialView("Partial/Comment", commentVM);
            }
            return Content("");
        }

        public ActionResult Delete(int id)
        {
            var deleteConfirmVM = new DeleteIssueConfirmationVM { ID = id };
            return PartialView("Partial/DeleteConfirm",deleteConfirmVM);
        }
        [HttpPost]
        [VerifyLogin]
        public ActionResult Delete(int id,string token="")
        {
            // to do : Check user permission
            try
            {
                var result=repo.DeleteIssue(id);
                if (result.Status)
                    return Json(new { Status = "Success" });
                else
                    log.Debug(result);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            return Json(new { Status = "Error" });
        }

        [HttpPost]
        public void SaveDueDate(string issueDueDate, int issueId)
        {
            try
            {
                bool isValidDate=false;
                var issue = repo.GetIssue(issueId);
                if (issue != null)
                {
                    if (!String.IsNullOrEmpty(issueDueDate))
                    {
                        issue.DueDate = DateTime.Parse(issueDueDate);
                        isValidDate = true;
                    }
                    else
                    {
                        issue.DueDate = DateTime.Now.AddYears(-1000);
                    }

                    var result = repo.SaveIssue(issue);
                    if (result.Status)
                    {
                        if (isValidDate)
                        {
                           var activity= issueService.SaveActivityForDueDate(issueId, TeamID, UserID);
                           if (activity != null)
                           {
                               var activityVM = issueService.GetActivityVM(activity);
                               var context = GlobalHost.ConnectionManager.GetHubContext<IssuesHub>();
                               context.Clients.Group(TeamID.ToString()).addNewTeamActivity(activityVM);
                           }

                        }
                    }
                    else
                    {
                        log.Error(result);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        public JsonResult comments(int id)
        {
            List<CommentVM> commentVMList = new List<CommentVM>();
            return Json(issueService.GetIssueCommentVMs(id),JsonRequestBehavior.AllowGet);            
        }


        #endregion public methods

        #region private methods
       
        private IssueListVM GetBugList(string iteration,int teamId, int size = 25)
        {
            var vm = new IssueListVM { CurrentTab = iteration, TeamID = teamId };
            List<IssueVM> issueList = issueService.GetIssueListVMs(iteration, teamId, size);
            vm.Bugs = issueList;
            // Set the user preference
            vm.IsCreateAndEditEnabled = CreateAndEditMode;
            return vm;
        }
        

        private void LoadDropDownsForCreate(CreateIssue viewModel)
        {
            viewModel.Projects = ProjectService.GetProjects(repo, TeamID);
            viewModel.Priorities = ProjectService.GetPriorities(repo);
            viewModel.Categories = ProjectService.GetCategories(repo);
            viewModel.Statuses = ProjectService.GetStatuses(repo);
            viewModel.Iterations = ProjectService.GetIterations();
        }

        private int SaveAttachedDocument(CreateIssue model, OperationStatus result, int fileCounter, HttpPostedFileBase file)
        {
            if (file != null)
            {
                fileCounter++;

                string fileName = Path.GetFileName(file.FileName).ToLower();
                string fileKey = fileName;
                fileKey = fileKey.Replace(" ", "-").Replace("%", "-");

                fileKey = String.Format("{0}-{1}-{2:n}-{3}", result.OperationID, fileCounter, Guid.NewGuid(), fileName);

                if (fileKey.Length > 99)
                    fileKey = fileKey.Substring(0, 99);

                string path = Path.Combine(Server.MapPath("~/uploads"), fileKey);
                file.SaveAs(path);

                Document img = new Document { FileName = fileName, ParentID = model.ID };
                img.FileAlias = fileKey;
                img.CreatedByID = UserID;
                img.ParentID = result.OperationID;
                var resultForImg = repo.SaveDocument(img);
                if (!resultForImg.Status)
                    log.Debug(resultForImg);
            }
            return fileCounter;
        }

        private void LoadDefaultIssueValues(Issue issue, CreateIssue model)
        {
            issue.PriorityID = model.SelectedPriority;
            if (issue.PriorityID == 0)
                issue.PriorityID = 1;

            issue.ProjectID = model.SelectedProject;
            if (issue.ProjectID == 0)
            {
                var teamMember = repo.GetTeamMember(UserID, TeamID);
                if (teamMember.DefaultProjectID == null)
                    throw new MissingSettingsException("Default Project not set", "Default Project");
                //get from team member 
                issue.ProjectID = teamMember.DefaultProjectID.Value;
            }
            issue.StatusID = model.SelectedStatus;
            if ((model.ID == 0) && (model.SelectedStatus == 0))
            {
                issue.StatusID = 1;
            }
            issue.CategoryID = model.SelectedCategory;
            if (issue.CategoryID == 0)
                issue.CategoryID = 1;

            issue.Location = (string.IsNullOrEmpty(model.SelectedIteration) ? "SPRNT" : model.SelectedIteration);

        }

        private Activity SaveActivity(CreateIssue model, Issue existingIssue,Issue issuePreviousVersion, Status currentIssueStatus, int issueId,bool isNewIssue=false)
        {
            bool isStateChanged = false;
            var activity = new Activity() { CreatedByID = UserID, ObjectID = issueId, ObjectType = "Issue" };

            if (isNewIssue)
            {
                activity.ActivityDesc = "Created";
                activity.NewState = model.Title;
                isStateChanged = true;
            }
            else
            {
                if (issuePreviousVersion.StatusID != existingIssue.StatusID)
                {
                    // status of issue updated
                    activity.OldState = model.Title;
                    activity.ActivityDesc = "Changed status";
                    activity.NewState = currentIssueStatus.Name;
                    isStateChanged = true;
                }
                                
            }

            activity.TeamID = TeamID;

            if (isStateChanged)
            {
                var result = repo.SaveActivity(activity);
                if (!result.Status)
                {
                    log.Error(result);
                }
                return repo.GetActivity(activity.ID);
                

            }
            return null;
        }

        
        #endregion private methods
       
    }
}
        
    

