﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SuiteCRMClient.Logging;
using SuiteCRMClient;
using SuiteCRMClient.RESTObjects;
using Newtonsoft.Json;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace SuiteCRMAddIn.BusinessLogic
{
    public class TaskSyncing: Syncing
    {
        List<TaskSyncState> lTaskItems;

        public TaskSyncing(SyncContext context)
            : base(context)
        {
        }

        public void StartTaskSync()
        {
            try
            {
                Log.Info("TaskSync thread started");
                Outlook.NameSpace oNS = this.Application.GetNamespace("mapi");
                Outlook.MAPIFolder taskFolder = GetDefaultFolder();
                Outlook.Items items = taskFolder.Items;

                items.ItemAdd -= TItems_ItemAdd;
                items.ItemChange -= TItems_ItemChange;
                items.ItemRemove -= TItems_ItemRemove;
                items.ItemAdd += TItems_ItemAdd;
                items.ItemChange += TItems_ItemChange;
                items.ItemRemove += TItems_ItemRemove;

                GetOutlookTItems(taskFolder);
                SyncTasks(taskFolder);

            }
            catch (Exception ex)
            {
                Log.Error("ThisAddIn.StartTaskSync", ex);
            }
            finally
            {
                Log.Info("TaskSync thread completed");
            }
        }
        private Outlook.OlImportance GetImportance(string sImportance)
        {
            Outlook.OlImportance oPriority = Outlook.OlImportance.olImportanceLow;
            switch (sImportance)
            {
                case "High":
                    oPriority = Outlook.OlImportance.olImportanceHigh;
                    break;
                case "Medium":
                    oPriority = Outlook.OlImportance.olImportanceNormal;
                    break;
            }
            return oPriority;
        }
        private Outlook.OlTaskStatus GetStatus(string sStatus)
        {
            Outlook.OlTaskStatus oStatus = Outlook.OlTaskStatus.olTaskNotStarted;
            switch (sStatus)
            {
                case "In Progress":
                    oStatus = Outlook.OlTaskStatus.olTaskInProgress;
                    break;
                case "Completed":
                    oStatus = Outlook.OlTaskStatus.olTaskComplete;
                    break;
                case "Deferred":
                    oStatus = Outlook.OlTaskStatus.olTaskDeferred;
                    break;

            }
            return oStatus;
        }
        private void SyncTasks(Outlook.MAPIFolder tasksFolder)
        {
            Log.Warn("SyncTasks");
            Log.Warn("My UserId= " + clsSuiteCRMHelper.GetUserId());
            try
            {
                int iOffset = 0;
                while (true)
                {
                    eGetEntryListResult _result2 = clsSuiteCRMHelper.GetEntryList("Tasks", "",
                                    0, "date_start DESC", iOffset, false, clsSuiteCRMHelper.GetSugarFields("Tasks"));
                    var nextOffset = _result2.next_offset;
                    if (iOffset == nextOffset)
                        break;

                    foreach (var oResult in _result2.entry_list)
                    {
                        try
                        {
                            UpdateFromCrm(tasksFolder, oResult);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("ThisAddIn.SyncTasks", ex);
                        }
                    }

                    iOffset = nextOffset;
                    if (iOffset == 0)
                        break;
                }
                try
                {
                    var lItemToBeDeletedO = lTaskItems.Where(a => !a.Touched && !string.IsNullOrWhiteSpace(a.OModifiedDate.ToString()));
                    foreach (var oItem in lItemToBeDeletedO)
                    {
                        oItem.OutlookItem.Delete();
                    }
                    lTaskItems.RemoveAll(a => !a.Touched && !string.IsNullOrWhiteSpace(a.OModifiedDate.ToString()));

                    var lItemToBeAddedToS = lTaskItems.Where(a => !a.Touched && string.IsNullOrWhiteSpace(a.OModifiedDate.ToString()));
                    foreach (var oItem in lItemToBeAddedToS)
                    {
                        AddTaskToS(oItem.OutlookItem);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("ThisAddIn.SyncTasks", ex);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ThisAddIn.SyncTasks", ex);
            }
        }

        private void UpdateFromCrm(Outlook.MAPIFolder tasksFolder, eEntryValue oResult)
        {
            dynamic dResult = JsonConvert.DeserializeObject(oResult.name_value_object.ToString());
            //
            if (clsSuiteCRMHelper.GetUserId() != dResult.assigned_user_id.value.ToString())
                return;

            DateTime? date_start = null;
            DateTime? date_due = null;

            string time_start = "--:--", time_due = "--:--";


            if (!string.IsNullOrWhiteSpace(dResult.date_start.value.ToString()) &&
                !string.IsNullOrEmpty(dResult.date_start.value.ToString()))
            {
                Log.Warn("    SET date_start = dResult.date_start");
                date_start = DateTime.ParseExact(dResult.date_start.value.ToString(), "yyyy-MM-dd HH:mm:ss", null);

                date_start = date_start.Value.Add(new DateTimeOffset(DateTime.Now).Offset);
                time_start =
                    TimeSpan.FromHours(date_start.Value.Hour)
                        .Add(TimeSpan.FromMinutes(date_start.Value.Minute))
                        .ToString(@"hh\:mm");
            }

            if (date_start != null && date_start < GetStartDate())
            {
                Log.Warn("    date_start=" + date_start.ToString() + ", GetStartDate= " + GetStartDate().ToString());
                return;
            }

            if (!string.IsNullOrWhiteSpace(dResult.date_due.value.ToString()))
            {
                date_due = DateTime.ParseExact(dResult.date_due.value.ToString(), "yyyy-MM-dd HH:mm:ss", null);
                date_due = date_due.Value.Add(new DateTimeOffset(DateTime.Now).Offset);
                time_due =
                    TimeSpan.FromHours(date_due.Value.Hour).Add(TimeSpan.FromMinutes(date_due.Value.Minute)).ToString(@"hh\:mm");
                ;
            }

            foreach (var lt in lTaskItems)
            {
                Log.Warn("    Task= " + lt.SEntryID);
            }

            var oItem = lTaskItems.FirstOrDefault(a => a.SEntryID == dResult.id.value.ToString());


            if (oItem == default(TaskSyncState))
            {
                Log.Warn("    if default");
                Outlook.TaskItem tItem = tasksFolder.Items.Add(Outlook.OlItemType.olTaskItem);
                tItem.Subject = dResult.name.value.ToString();

                if (!string.IsNullOrWhiteSpace(dResult.date_start.value.ToString()))
                {
                    tItem.StartDate = date_start.Value;
                }
                if (!string.IsNullOrWhiteSpace(dResult.date_due.value.ToString()))
                {
                    tItem.DueDate = date_due.Value; // DateTime.Parse(dResult.date_due.value.ToString());
                }

                string body = dResult.description.value.ToString();
                tItem.Body = string.Concat(body, "#<", time_start, "#", time_due);
                tItem.Status = GetStatus(dResult.status.value.ToString());
                tItem.Importance = GetImportance(dResult.priority.value.ToString());

                Outlook.UserProperty oProp = tItem.UserProperties.Add("SOModifiedDate", Outlook.OlUserPropertyType.olText);
                oProp.Value = dResult.date_modified.value.ToString();
                Outlook.UserProperty oProp2 = tItem.UserProperties.Add("SEntryID", Outlook.OlUserPropertyType.olText);
                oProp2.Value = dResult.id.value.ToString();
                lTaskItems.Add(new TaskSyncState
                {
                    OutlookItem = tItem,
                    OModifiedDate = DateTime.ParseExact(dResult.date_modified.value.ToString(), "yyyy-MM-dd HH:mm:ss", null),
                    SEntryID = dResult.id.value.ToString(),
                    Touched = true
                });
                Log.Warn("    save 0");
                tItem.Save();
            }
            else
            {
                Log.Warn("    else not default");
                oItem.Touched = true;
                Outlook.TaskItem tItem = oItem.OutlookItem;
                Outlook.UserProperty oProp = tItem.UserProperties["SOModifiedDate"];

                Log.Warn(
                    (string)
                    ("    oProp.Value= " + oProp.Value + ", dResult.date_modified=" + dResult.date_modified.value.ToString()));
                if (oProp.Value != dResult.date_modified.value.ToString())
                {
                    tItem.Subject = dResult.name.value.ToString();

                    if (!string.IsNullOrWhiteSpace(dResult.date_start.value.ToString()))
                    {
                        Log.Warn("    tItem.StartDate= " + tItem.StartDate + ", date_start=" + date_start);
                        tItem.StartDate = date_start.Value;
                    }
                    if (!string.IsNullOrWhiteSpace(dResult.date_due.value.ToString()))
                    {
                        tItem.DueDate = date_due.Value; // DateTime.Parse(dResult.date_due.value.ToString());
                    }

                    string body = dResult.description.value.ToString();
                    tItem.Body = string.Concat(body, "#<", time_start, "#", time_due);
                    tItem.Status = GetStatus(dResult.status.value.ToString());
                    tItem.Importance = GetImportance(dResult.priority.value.ToString());
                    if (oProp == null)
                        oProp = tItem.UserProperties.Add("SOModifiedDate", Outlook.OlUserPropertyType.olText);
                    oProp.Value = dResult.date_modified.value.ToString();
                    Outlook.UserProperty oProp2 = tItem.UserProperties["SEntryID"];
                    if (oProp2 == null)
                        oProp2 = tItem.UserProperties.Add("SEntryID", Outlook.OlUserPropertyType.olText);
                    oProp2.Value = dResult.id.value.ToString();
                    Log.Warn("    save 1");
                    tItem.Save();
                }
                oItem.OModifiedDate = DateTime.ParseExact(dResult.date_modified.value.ToString(), "yyyy-MM-dd HH:mm:ss", null);
            }
        }

        private void GetOutlookTItems(Outlook.MAPIFolder taskFolder)
        {
            try
            {
                if (lTaskItems == null)
                {
                    lTaskItems = new List<TaskSyncState>();
                    Outlook.Items items = taskFolder.Items; //.Restrict("[MessageClass] = 'IPM.Task'" + GetStartDateString());
                    foreach (Outlook.TaskItem oItem in items)
                    {
                        if (oItem.DueDate < DateTime.Now.AddDays(-5))
                            continue;
                        Outlook.UserProperty oProp = oItem.UserProperties["SOModifiedDate"];
                        if (oProp != null)
                        {
                            Outlook.UserProperty oProp2 = oItem.UserProperties["SEntryID"];
                            lTaskItems.Add(new TaskSyncState
                            {
                                OutlookItem = oItem,
                                //OModifiedDate = "Fresh",
                                OModifiedDate = DateTime.UtcNow,

                                SEntryID = oProp2.Value.ToString()
                            });
                        }
                        else
                        {
                            lTaskItems.Add(new TaskSyncState
                            {
                                OutlookItem = oItem
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ThisAddIn.GetOutlookTItems", ex);
            }
        }

        void TItems_ItemChange(object Item)
        {
            Log.Warn("TItems_ItemChange");
            try
            {
                var oItem = Item as Outlook.TaskItem;
                string entryId = oItem.EntryID;
                Log.Warn("    oItem.EntryID= " + entryId);

                TaskSyncState taskitem = lTaskItems.FirstOrDefault(a => a.OutlookItem.EntryID == entryId);
                if (taskitem != default(TaskSyncState))
                {
                    if ((DateTime.UtcNow - taskitem.OModifiedDate).TotalSeconds > 5)
                    {
                        Log.Warn("2 callitem.IsUpdate = " + taskitem.IsUpdate);
                        taskitem.IsUpdate = 0;
                    }

                    Log.Warn("Before UtcNow - callitem.OModifiedDate= " + (DateTime.UtcNow - taskitem.OModifiedDate).TotalSeconds.ToString());

                    if ((int)(DateTime.UtcNow - taskitem.OModifiedDate).TotalSeconds > 2 && taskitem.IsUpdate == 0)
                    {
                        taskitem.OModifiedDate = DateTime.UtcNow;
                        Log.Warn("1 callitem.IsUpdate = " + taskitem.IsUpdate);
                        taskitem.IsUpdate++;
                    }

                    Log.Warn("callitem = " + taskitem.OutlookItem.Subject);
                    Log.Warn("callitem.SEntryID = " + taskitem.SEntryID);
                    Log.Warn("callitem mod_date= " + taskitem.OModifiedDate.ToString());
                    Log.Warn("UtcNow - callitem.OModifiedDate= " + (DateTime.UtcNow - taskitem.OModifiedDate).TotalSeconds.ToString());
                }
                else
                {
                    Log.Warn("not found callitem ");
                }


                if (IsTaskView && lTaskItems.Exists(a => a.OutlookItem.EntryID == entryId //// if (IsTaskView && lTaskItems.Exists(a => a.oItem.EntryID == entryId && a.OModifiedDate != "Fresh"))
                                 && taskitem.IsUpdate == 1
                                 )
                )
                {

                    Outlook.UserProperty oProp1 = oItem.UserProperties["SEntryID"];
                    if (oProp1 != null)
                    {
                        Log.Warn("    go to AddTaskToS");
                        taskitem.IsUpdate++;
                        AddTaskToS(oItem, oProp1.Value.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ThisAddIn.TItems_ItemChange", ex);
            }
        }

        void TItems_ItemAdd(object Item)
        {
            try
            {
                if (IsTaskView)
                {
                    var item = Item as Outlook.TaskItem;
                    Outlook.UserProperty oProp2 = item.UserProperties["SEntryID"];  // to avoid duplicating of the task
                    if (oProp2 != null)
                    {
                        AddTaskToS(item, oProp2.Value);
                    }
                    else
                    {
                        AddTaskToS(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ThisAddIn.TItems_ItemAdd", ex);
            }
        }
        private void AddTaskToS(Outlook.TaskItem oItem, string sID = "")
        {
            Log.Warn("AddTaskToS");
            //if (!settings.SyncCalendar)
            //    return;
            if (oItem != null)
            {
                try
                {
                    string _result = "";
                    eNameValue[] data = new eNameValue[7];
                    string strStatus = "";
                    string strImportance = "";
                    switch (oItem.Status)
                    {
                        case Outlook.OlTaskStatus.olTaskNotStarted:
                            strStatus = "Not Started";
                            break;
                        case Outlook.OlTaskStatus.olTaskInProgress:
                            strStatus = "In Progress";
                            break;
                        case Outlook.OlTaskStatus.olTaskComplete:
                            strStatus = "Completed";
                            break;
                        case Outlook.OlTaskStatus.olTaskDeferred:
                            strStatus = "Deferred";
                            break;
                    }
                    switch (oItem.Importance)
                    {
                        case Outlook.OlImportance.olImportanceLow:
                            strImportance = "Low";
                            break;

                        case Outlook.OlImportance.olImportanceNormal:
                            strImportance = "Medium";
                            break;

                        case Outlook.OlImportance.olImportanceHigh:
                            strImportance = "High";
                            break;
                    }

                    DateTime uTCDateTime = new DateTime();
                    DateTime time2 = new DateTime();
                    uTCDateTime = oItem.StartDate.ToUniversalTime();
                    if (oItem.DueDate != null)
                        time2 = oItem.DueDate.ToUniversalTime();

                    string body = "";
                    string str, str2;
                    str = str2 = "";
                    if (oItem.Body != null)
                    {
                        body = oItem.Body.ToString();
                        var times = this.ParseTimesFromTaskBody(body);
                        if (times != null)
                        {
                            uTCDateTime = uTCDateTime.Add(times[0]);
                            time2 = time2.Add(times[1]);

                            //check max date, date must has value !
                            if (uTCDateTime.ToUniversalTime().Year < 4000)
                                str = string.Format("{0:yyyy-MM-dd HH:mm:ss}", uTCDateTime.ToUniversalTime());
                            if (time2.ToUniversalTime().Year < 4000)
                                str2 = string.Format("{0:yyyy-MM-dd HH:mm:ss}", time2.ToUniversalTime());
                        }
                        else
                        {
                            str = oItem.StartDate.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
                            str2 = oItem.DueDate.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
                        }

                    }
                    else
                    {
                        str = oItem.StartDate.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
                        str2 = oItem.DueDate.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    }

                    //str = "2016-11-10 11:34:01";
                    //str2 = "2016-11-19 11:34:01";


                    string description = "";

                    if (!string.IsNullOrEmpty(body))
                    {
                        int lastIndex = body.LastIndexOf("#<");
                        if (lastIndex >= 0)
                            description = body.Remove(lastIndex);
                        else
                        {
                            description = body;
                        }
                    }
                    Log.Warn("    description= " + description);

                    data[0] = clsSuiteCRMHelper.SetNameValuePair("name", oItem.Subject);
                    data[1] = clsSuiteCRMHelper.SetNameValuePair("description", description);
                    data[2] = clsSuiteCRMHelper.SetNameValuePair("status", strStatus);
                    data[3] = clsSuiteCRMHelper.SetNameValuePair("date_due", str2);
                    data[4] = clsSuiteCRMHelper.SetNameValuePair("date_start", str);
                    data[5] = clsSuiteCRMHelper.SetNameValuePair("priority", strImportance);

                    if (sID == "")
                        data[6] = clsSuiteCRMHelper.SetNameValuePair("assigned_user_id", clsSuiteCRMHelper.GetUserId());
                    else
                        data[6] = clsSuiteCRMHelper.SetNameValuePair("id", sID);

                    _result = clsSuiteCRMHelper.SetEntryUnsafe(data, "Tasks");
                    Outlook.UserProperty oProp = oItem.UserProperties["SOModifiedDate"];
                    if (oProp == null)
                        oProp = oItem.UserProperties.Add("SOModifiedDate", Outlook.OlUserPropertyType.olText);
                    oProp.Value = DateTime.UtcNow;
                    Outlook.UserProperty oProp2 = oItem.UserProperties["SEntryID"];
                    if (oProp2 == null)
                        oProp2 = oItem.UserProperties.Add("SEntryID", Outlook.OlUserPropertyType.olText);
                    oProp2.Value = _result;
                    string entryId = oItem.EntryID;
                    oItem.Save();

                    var sItem = lTaskItems.FirstOrDefault(a => a.OutlookItem.EntryID == entryId);
                    if (sItem != default(TaskSyncState))
                    {
                        sItem.Touched = true;
                        sItem.OutlookItem = oItem;
                        sItem.OModifiedDate = DateTime.UtcNow;
                        sItem.SEntryID = _result;
                    }
                    else
                        lTaskItems.Add(new TaskSyncState { Touched = true, SEntryID = _result, OModifiedDate = DateTime.UtcNow, OutlookItem = oItem });

                    Log.Warn("    date_start= " + str + ", date_due=" + str2);
                }
                catch (Exception ex)
                {
                    Log.Error("ThisAddIn.AddTaskToS", ex);
                }
            }
        }
        void TItems_ItemRemove()
        {
            if (IsTaskView && false)
            {
                try
                {
                    foreach (var oItem in lTaskItems)
                    {
                        try
                        {
                            string sID = oItem.OutlookItem.EntryID;
                        }
                        catch (COMException)
                        {
                            eNameValue[] data = new eNameValue[2];
                            data[0] = clsSuiteCRMHelper.SetNameValuePair("id", oItem.SEntryID);
                            data[1] = clsSuiteCRMHelper.SetNameValuePair("deleted", "1");
                            clsSuiteCRMHelper.SetEntryUnsafe(data, "Tasks");
                            oItem.Delete = true;
                        }
                    }
                    lTaskItems.RemoveAll(a => a.Delete);
                }
                catch (Exception ex)
                {
                    Log.Error("ThisAddIn.TItems_ItemRemove", ex);
                }
            }
        }

        private TimeSpan[] ParseTimesFromTaskBody(string body)
        {
            try
            {
                if (string.IsNullOrEmpty(body))
                    return null;
                TimeSpan[] timesToAdd = new TimeSpan[2];
                List<int> hhmm = new List<int>(4);

                string times = body.Substring(body.LastIndexOf("#<")).Substring(2);
                char[] sep = { '<', '#', ':' };
                int parsed = 0;
                foreach (var digit in times.Split(sep))
                {
                    int.TryParse(digit, out parsed);
                    hhmm.Add(parsed);
                    parsed = 0;
                }

                TimeSpan start_time = TimeSpan.FromHours(hhmm[0]).Add(TimeSpan.FromMinutes(hhmm[1]));
                TimeSpan due_time = TimeSpan.FromHours(hhmm[2]).Add(TimeSpan.FromMinutes(hhmm[3]));
                timesToAdd[0] = start_time;
                timesToAdd[1] = due_time;
                return timesToAdd;
            }
            catch
            {
                Log.Warn("Body doesn't have time string");
                return null;
            }
        }

        public Outlook.MAPIFolder GetDefaultFolder()
        {
            return Application.Session.GetDefaultFolder(Outlook.OlDefaultFolders.olFolderTasks);
        }

        protected bool IsTaskView => Context.CurrentFolderItemType == Outlook.OlItemType.olTaskItem;
    }
}
