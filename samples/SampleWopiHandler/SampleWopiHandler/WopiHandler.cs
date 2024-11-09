using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Web;
using System.Diagnostics;
using Newtonsoft.Json; // For JsonConvert.SerializeObject

namespace SampleWopiHandler
{
    /// <summary>
    /// This class implements a simple WOPI handler that allows users to read and write files
    /// stored on the local filesystem.
    /// </summary>
    /// <remarks>
    /// The implementation of these WOPI methods is for illustrative purposes only.
    /// A real WOPI system would verify user permissions, store locks in a persistent way, etc.
    /// </remarks>
    public class WopiHandler : IHttpHandler
    {
        private const string WopiPath = @"/wopi/";
        private const string FilesRequestPath = @"files/";
        private const string FoldersRequestPath = @"folders/";
        private const string ContentsRequestPath = @"/contents";
        private const string ChildrenRequestPath = @"/children";
        internal const string LocalStoragePath = @"c:\WopiStorage\";
        internal const string samplePutRelativeViewTemplate = @"c:\inetpub\wwwroot\SampleHostPage-hostview-putrelative-template.html";
        internal const string samplePutRelativeView = @"c:\inetpub\wwwroot\SampleHostPage-hostview-putrelative.html";
        internal const string samplePutRelativeEditTemplate = @"c:\inetpub\wwwroot\SampleHostPage-hostedit-putrelative-template.html";
        internal const string samplePutRelativeEdit = @"c:\inetpub\wwwroot\SampleHostPage-hostedit-putrelative.html";

        private class LockInfo
        {
            public string Lock { get; set; }
            public DateTime DateCreated { get; set; }
            public bool Expired { get { return this.DateCreated.AddMinutes(30) < DateTime.UtcNow; } }
        }

        /// <summary>
        /// Simplified Lock info storage.
        /// A real lock implementation would use persised storage for locks.
        /// </summary>
        private static readonly Dictionary<string, LockInfo> Locks = new Dictionary<string, LockInfo>();
        // BUG 2245932
        // Set this to true to test lock string modification 
        private bool test_2245932 = false;

        #region IHttpHandler Members

        public bool IsReusable
        {
            // Return false in case your Managed Handler cannot be reused for another request.
            // This would be false if you have some state information preserved per request.
            get { return true; }
        }

        /// <summary>
        /// Begins processing the incoming WOPI request.
        /// </summary>
        public void ProcessRequest(HttpContext context)
        {
            // WOPI ProofKey validation is an optional way that a WOPI host can ensure that the request
            // is coming from the Office server that they expect to be talking to.
            if (!ValidateWopiProofKey(context.Request))
            {
                context.Response.AddHeader("WopiHandlerDiag", "Proof Key Validation failure!");
                ReturnServerError(context.Response);
            }

            // Parse the incoming WOPI request
            WopiRequest requestData = ParseRequest(context.Request);

            // Call the appropriate handler for the WOPI request we received
            switch (requestData.Type)
            {
                case RequestType.CheckFileInfo:
                    HandleCheckFileInfoRequest(context, requestData);
                    break;

                case RequestType.Lock:
                    HandleLockRequest(context, requestData);
                    break;

                case RequestType.Unlock:
                    HandleUnlockRequest(context, requestData);
                    break;

                case RequestType.RefreshLock:
                    HandleRefreshLockRequest(context, requestData);
                    break;

                case RequestType.UnlockAndRelock:
                    HandleUnlockAndRelockRequest(context, requestData);
                    break;

                case RequestType.GetFile:
                    HandleGetFileRequest(context, requestData);
                    break;

                case RequestType.PutFile:
                    HandlePutFileRequest(context, requestData);
                    break;

                // These request types are not implemented in this sample.
                // Of these, only PutRelativeFile would be implemented by a typical WOPI host.
                case RequestType.PutRelativeFile: // If this is implemented, the UserCanNotWriteRelative field in CheckFileInfo needs to be updated.
                    HandlePutRelativeFileRequest(context, requestData);
                    break;

                case RequestType.EnumerateChildren:
                case RequestType.CheckFolderInfo:
                case RequestType.DeleteFile:
                case RequestType.ExecuteCobaltRequest:
                case RequestType.GetRestrictedLink:
                case RequestType.ReadSecureStore:
                case RequestType.RevokeRestrictedLink:
                    ReturnUnsupported(context.Response);
                    break;

                default:
                    ReturnServerError(context.Response);
                    break;
            }
        }

        #endregion

        /// <summary>
        /// Parse the request determine the request type, access token, and file id.
        /// For more details, see the [MS-WOPI] Web Application Open Platform Interface Protocol specification.
        /// </summary>
        /// <remarks>
        /// Can be extended to parse client version, machine name, etc.
        /// </remarks>
        private static WopiRequest ParseRequest(HttpRequest request)
        {

            // Initilize wopi request data object with default values
            WopiRequest requestData = new WopiRequest()
            {
                Type = RequestType.None,
                AccessToken = request.QueryString["access_token"],
                Id = ""
            };

            // request.Url pattern:
            // http(s)://server/<...>/wopi/[files|folders]/<id>?access_token=<token>
            // or
            // https(s)://server/<...>/wopi/files/<id>/contents?access_token=<token>
            // or
            // https(s)://server/<...>/wopi/folders/<id>/children?access_token=<token>

            // Get request path, e.g. /<...>/wopi/files/<id>
            string requestPath = request.Url.AbsolutePath;
            // remove /<...>/wopi/
            string wopiPath = requestPath.Substring(WopiPath.Length);

            if (wopiPath.StartsWith(FilesRequestPath))
            {
                // A file-related request

                // remove /files/ from the beginning of wopiPath
                string rawId = wopiPath.Substring(FilesRequestPath.Length);

                if (rawId.EndsWith(ContentsRequestPath))
                {
                    // The rawId ends with /contents so this is a request to read/write the file contents

                    // Remove /contents from the end of rawId to get the actual file id
                    requestData.Id = rawId.Substring(0, rawId.Length - ContentsRequestPath.Length);

                    if (request.HttpMethod == "GET")
                        requestData.Type = RequestType.GetFile;
                    if (request.HttpMethod == "POST")
                        requestData.Type = RequestType.PutFile;
                }
                else
                {
                    requestData.Id = rawId;

                    if (request.HttpMethod == "GET")
                    {
                        // a GET to the file is always a CheckFileInfo request
                        requestData.Type = RequestType.CheckFileInfo;
                    }
                    else if (request.HttpMethod == "POST")
                    {
                        // For a POST to the file we need to use the X-WOPI-Override header to determine the request type
                        string wopiOverride = request.Headers[WopiHeaders.RequestType];

                        switch (wopiOverride)
                        {
                            case "PUT_RELATIVE":
                                requestData.Type = RequestType.PutRelativeFile;
                                break;
                            case "LOCK":
                                // A lock could be either a lock or an unlock and relock, determined based on whether
                                // the request sends an OldLock header.
                                if (request.Headers[WopiHeaders.OldLock] != null)
                                    requestData.Type = RequestType.UnlockAndRelock;
                                else
                                    requestData.Type = RequestType.Lock;
                                break;
                            case "UNLOCK":
                                requestData.Type = RequestType.Unlock;
                                break;
                            case "REFRESH_LOCK":
                                requestData.Type = RequestType.RefreshLock;
                                break;
                            case "COBALT":
                                requestData.Type = RequestType.ExecuteCobaltRequest;
                                break;
                            case "DELETE":
                                requestData.Type = RequestType.DeleteFile;
                                break;
                            case "READ_SECURE_STORE":
                                requestData.Type = RequestType.ReadSecureStore;
                                break;
                            case "GET_RESTRICTED_LINK":
                                requestData.Type = RequestType.GetRestrictedLink;
                                break;
                            case "REVOKE_RESTRICTED_LINK":
                                requestData.Type = RequestType.RevokeRestrictedLink;
                                break;
                        }
                    }
                }
            }
            else if (wopiPath.StartsWith(FoldersRequestPath))
            {
                // A folder-related request.

                // remove /folders/ from the beginning of wopiPath
                string rawId = wopiPath.Substring(FoldersRequestPath.Length);

                if (rawId.EndsWith(ChildrenRequestPath))
                {
                    // rawId ends with /children, so it's an EnumerateChildren request.

                    // remove /children from the end of rawId
                    requestData.Id = rawId.Substring(0, rawId.Length - ChildrenRequestPath.Length);
                    requestData.Type = RequestType.EnumerateChildren;
                }
                else
                {
                    // rawId doesn't end with /children, so it's a CheckFolderInfo.

                    requestData.Id = rawId;
                    requestData.Type = RequestType.CheckFolderInfo;
                }
            }
            else
            {
                // An unknown request.
                requestData.Type = RequestType.None;
            }

            return requestData;
        }

        #region Processing for each of the WOPI operations

        /// <summary>
        /// Processes a CheckFileInfo request
        /// </summary>
        /// <remarks>
        /// For full documentation on CheckFileInfo, see https://wopi.readthedocs.org/en/latest/wopi/files/CheckFileInfo.html
        /// </remarks>
        private void HandleCheckFileInfoRequest(HttpContext context, WopiRequest requestData)
        {
            System.Diagnostics.Debugger.Log(1, "SampleWopiHandler", "Enterred HandleCheckFileInfoRequest");
            System.Diagnostics.Trace.TraceInformation("Enterred HandleCheckFileInfoRequest");
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (!File.Exists(requestData.FullPath))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            try
            {
                FileInfo fileInfo = new FileInfo(requestData.FullPath);

                if (!fileInfo.Exists)
                {
                    ReturnFileUnknown(context.Response);
                    return;
                }
                FileStream fileStream = File.Open(requestData.FullPath, FileMode.Open, FileAccess.Read, FileShare.None);
                String ContentSha256 = "";
                //int ContentLength = (int)fileStream.Length;
                //MemoryStream _contentStream = new MemoryStream(ContentLength);
                //fileStream.CopyTo(_contentStream);
                fileStream.Seek(0, SeekOrigin.Begin);
                using (System.Security.Cryptography.HashAlgorithm hashAlgorithm = new System.Security.Cryptography.SHA256CryptoServiceProvider())
                {
                    ContentSha256 = Convert.ToBase64String(hashAlgorithm.ComputeHash(fileStream));
                }
                fileStream.Seek(0, SeekOrigin.Begin);
                fileStream.Close();
                fileStream.Dispose();
                string[] arrResult = context.Request.RawUrl.Split('/');
                String downloadurl = arrResult[arrResult.GetUpperBound(0)];
                // For more info on CheckFileInfoResponse fields, see https://wopi.readthedocs.org/en/latest/wopi/files/CheckFileInfo.html#response
                CheckFileInfoResponse responseData = new CheckFileInfoResponse()
                {
                    // required CheckFileInfo properties
                    BaseFileName = Path.GetFileName(requestData.FullPath),
                    OwnerId = "documentOwnerId",
                    Size = (int)fileInfo.Length,
                    UserId = "user@contoso.com",
                    //Version = @"2017 - 12 - 28T16:53:07.7811341Z",
                    Version = fileInfo.LastWriteTimeUtc.ToString("O" /* ISO 8601 DateTime format string */), // Using the file write time is an arbitrary choice.

                    // optional CheckFileInfo properties
                    AllowExternalMarketplace = true,
                    BreadcrumbBrandName = "LocalStorage WOPI Host",
                    BreadcrumbFolderName = fileInfo.Directory != null ? fileInfo.Directory.Name : "",
                    BreadcrumbDocName = Path.GetFileNameWithoutExtension(requestData.FullPath),
                    BreadcrumbBrandUrl = "http://" + context.Request.Url.Host,
                    BreadcrumbFolderUrl = "http://" + context.Request.Url.Host,

                    UserFriendlyName = "A WOPI User",

                    PostMessageOrigin = "http://localhost/samplehostpage.html",
                    CloseUrl = "http://onedrive.com",
                    //CloseUrl = "http://localhost/samplehostpage.html",
                    ClientUrl = "file://JEEBS5/WopiStorage/196_7.docx",
                    //HostViewUrl = "http://localhost/samplehostpage.html",
                    //HostEditUrl = "http://localhost/samplehostpage-hostediturl.html",
                    HostEditUrl = "http://onedrive.com",
                    //DownloadUrl = "https://jebosoft-my.sharepoint.com/:w:/r/personal/tomjebo_jebosoft_onmicrosoft_com/Documents/test2.docx?download=1",
                    //DownloadUrl = "http://jeebshpzb:8888/download/" + downloadurl,
                    DownloadUrl = "https://go.microsoft.com/fwlink/?LinkID=521962",

                    SupportsCobalt = false, // for 2013 only
                    SupportsCoauth = false, // for 2013 only
                    SupportsEditing = true,
                    SupportsLocks = true,
                    ClosePostMessage = false,
                    CloseButtonClosesWindow = false,
                    SupportsUpdate = true,
                    UserCanNotWriteRelative = false, /* Because this host does not support PutRelativeFile */
                    SupportsFolders = true, // for 2013 only
                    SupportsScenarioLinks = false, // for 2013 only 
                    SupportsSecureStore = true, // for 2013 only 
                    WebEditingDisabled = false, // for 2013 only 
                    // mibowe forum case test
                    RestrictedWebViewOnly = true, // for 2013 only 
                    ProtectInClient = false,

                    // mibowe forum case test
                    ReadOnly = true, // fileInfo.IsReadOnly,
                    UserCanWrite = !fileInfo.IsReadOnly,
                    SHA256 = ContentSha256,
                };

                string jsonString = JsonConvert.SerializeObject(responseData);

                context.Response.Write(jsonString);
                ReturnSuccess(context.Response);
            }
            catch (UnauthorizedAccessException)
            {

                ReturnFileUnknown(context.Response);
            }
        }

        /// <summary>
        /// Processes a GetFile request
        /// </summary>
        /// <remarks>
        /// For full documentation on GetFile, see https://wopi.readthedocs.org/en/latest/wopi/file_contents/GetFile.html
        /// </remarks>
        private void HandleGetFileRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: false))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (!File.Exists(requestData.FullPath))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            try
            {
                // transmit file from local storage to the response stream.
                context.Response.TransmitFile(requestData.FullPath);
                context.Response.AddHeader(WopiHeaders.ItemVersion, GetFileVersion(requestData.FullPath));
                ReturnSuccess(context.Response);
            }
            catch (UnauthorizedAccessException)
            {
                ReturnFileUnknown(context.Response);
            }
            catch (FileNotFoundException)
            {
                ReturnFileUnknown(context.Response);
            }
        }

        private static string GetFileVersion(string filename)
        {
            FileInfo fileInfo = new FileInfo(filename);
            return fileInfo.LastWriteTimeUtc.ToString("O" /* ISO 8601 DateTime format string */); // Using the file write time is an arbitrary choice.
        }

        /// <summary>
        /// Processes a PutRelativeFile request
        /// </summary>
        /// <remarks>
        /// For full documentation on PutFile, see https://wopi.readthedocs.org/en/latest/wopi/file_contents/PutFile.html 
        /// For X-WOPI-RelativeTarget (indicates specific mode) or X-WOPI-SuggestedTarget (indicates suggested mode) request headers,
        ///       we will ignore these and just overwrite the existing target file if needed.
        /// </remarks>
        private void HandlePutRelativeFileRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            string newFile = "";
            if (context.Request.Headers["X-WOPI-RelativeTarget"] != null)
                newFile = context.Request.Headers["X-WOPI-RelativeTarget"];
            else if (context.Request.Headers["X-WOPI-SuggestedTarget"].Substring(0, 1) == ".")
                newFile = Path.Combine(WopiHandler.LocalStoragePath, requestData.Id + context.Request.Headers["X-WOPI-SuggestedTarget"]);
            else
                newFile = Path.Combine(WopiHandler.LocalStoragePath, context.Request.Headers["X-WOPI-SuggestedTarget"]);

            if (!File.Exists(requestData.FullPath))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];
            LockInfo existingLock;
            bool hasExistingLock;

            // Only need to check lock for new file.
            lock (Locks)
            {
                hasExistingLock = TryGetLock(newFile, out existingLock);
            }

            if (hasExistingLock && existingLock.Lock != newLock)
            {
                // lock mismatch/locked by another interface

                ReturnLockMismatch(context.Response, ClusterLockString(existingLock.Lock));

                return;
            }

            FileInfo putTargetFileInfo = new FileInfo(requestData.FullPath);

            //// The WOPI spec allows for a PutFile to succeed on a non-locked file if the file is currently zero bytes in length.
            //// This allows for a more efficient Create New File flow that saves the Lock roundtrips.
            //if (!hasExistingLock && putTargetFileInfo.Length != 0)
            //{
            //	// With no lock and a non-zero file, a PutFile could potentially result in data loss by clobbering
            //	// existing content.  Therefore, return a lock mismatch error.
            //	ReturnLockMismatch(context.Response, reason: "PutFile on unlocked file with current size != 0");
            //}

            // Either the file has a valid lock that matches the lock in the request, or the file is unlocked
            // and is zero bytes.  Either way, proceed with the PutFile.
            try
            {
                string targetFile = Path.Combine(WopiHandler.LocalStoragePath, newFile);
                // TODO: Should be replaced with proper file save logic to a real storage system and ensures write atomicity
                //using (var fileStreamExisting = File.Open(requestData.FullPath, FileMode.Open, FileAccess.Write, FileShare.None))
                using (var fileStreamNew = File.Open(targetFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    context.Request.InputStream.CopyTo(fileStreamNew);
                }

                var sb = new StringBuilder();

                // create the host page at which the hostediturl parameter can point.
                using (var sr = new StreamWriter(samplePutRelativeEdit))
                {
                    sb.Append(PutRelativeFileResponse.samplehostfile_first);
                    sb.Append(Path.GetFileName(newFile));
                    sb.Append(PutRelativeFileResponse.samplehostfile_second);
                    sb.Append(Path.GetFileName(newFile));
                    sb.Append(PutRelativeFileResponse.samplehostfile_third);
                    sr.Write(sb.ToString());
                }

                PutRelativeFileResponse responseData = new PutRelativeFileResponse()
                {
                    Url = @"http%3A%2F%2Fjeebshpzb:8888%2Fwopi%2Ffiles%2F" + newFile + @"&access_token=DEADBEEFDEADBEEFDEADBEEF&access_token_ttl=0",
                    Name = newFile,

                    HostViewUrl = @"http://jeebshpzb:8888/samplehostpage-hostview-putrelative.html",
                    HostEditUrl = @"http://jeebshpzb:8888/samplehostpage-hostedit-putrelative.html",
                };

                string jsonString = JsonConvert.SerializeObject(responseData);

                context.Response.Write(jsonString);
                ReturnSuccess(context.Response);
            }
            catch (UnauthorizedAccessException)
            {
                ReturnFileUnknown(context.Response);
            }
            catch (IOException)
            {
                ReturnServerError(context.Response);
            }
        }

        /// <summary>
        /// Processes a PutFile request
        /// </summary>
        /// <remarks>
        /// For full documentation on PutFile, see https://wopi.readthedocs.org/en/latest/wopi/file_contents/PutFile.html
        /// </remarks>
        private void HandlePutFileRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (!File.Exists(requestData.FullPath))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];
            LockInfo existingLock;
            bool hasExistingLock;

            lock (Locks)
            {
                hasExistingLock = TryGetLock(requestData.Id, out existingLock);
            }

            if (hasExistingLock && existingLock.Lock != newLock)
            {
                // lock mismatch/locked by another interface
                ReturnLockMismatch(context.Response, ClusterLockString(existingLock.Lock));
                return;
            }

            FileInfo putTargetFileInfo = new FileInfo(requestData.FullPath);

            // The WOPI spec allows for a PutFile to succeed on a non-locked file if the file is currently zero bytes in length.
            // This allows for a more efficient Create New File flow that saves the Lock roundtrips.
            if (!hasExistingLock && putTargetFileInfo.Length != 0)
            {
                // With no lock and a non-zero file, a PutFile could potentially result in data loss by clobbering
                // existing content.  Therefore, return a lock mismatch error.
                ReturnLockMismatch(context.Response, reason: "PutFile on unlocked file with current size != 0");
            }

            // Either the file has a valid lock that matches the lock in the request, or the file is unlocked
            // and is zero bytes.  Either way, proceed with the PutFile.
            try
            {
                // TODO: Should be replaced with proper file save logic to a real storage system and ensures write atomicity
                using (var fileStream = File.Open(requestData.FullPath, FileMode.Truncate, FileAccess.Write, FileShare.None))
                {
                    context.Request.InputStream.CopyTo(fileStream);
                }
                context.Response.AddHeader(WopiHeaders.ItemVersion, GetFileVersion(requestData.FullPath));
            }
            catch (UnauthorizedAccessException)
            {
                ReturnFileUnknown(context.Response);
            }
            catch (IOException)
            {
                ReturnServerError(context.Response);
            }
        }

        // for testing ONLY
        // BREAKS PROTOCOL
        // remove after testing.
        private string ClusterLockString(string @lock)
        {
            if ((!test_2245932) || (@lock.Contains("\"C\":\"\"")))
                return @lock;
            else
            {
                StringBuilder clusterString = new StringBuilder(@lock);
                clusterString.Insert(clusterString.ToString().IndexOf("}"), ", \"C\":\"\"");
                return clusterString.ToString();
            }
        }

        /// <summary>
        /// Processes a Lock request
        /// </summary>
        /// <remarks>
        /// For full documentation on Lock, see https://wopi.readthedocs.org/en/latest/wopi/files/Lock.html
        /// </remarks>
        private void HandleLockRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (!File.Exists(requestData.FullPath))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];

            lock (Locks)
            {
                LockInfo existingLock;
                bool fLocked = TryGetLock(requestData.Id, out existingLock);
                if (fLocked && existingLock.Lock != newLock)
                {
                    // There is a valid existing lock on the file and it doesn't match the requested lockstring.

                    // This is a fairly common case and shouldn't be tracked as an error.  Office Online can store
                    // information about a current session in the lock value and expects to conflict when there's
                    // an existing session to join.
                    ReturnLockMismatch(context.Response, ClusterLockString(existingLock.Lock));
                }
                else
                {
                    // The file is not currently locked or the lock has already expired or existing lock matches the requested lock.
                    if (fLocked)
                        Locks.Remove(requestData.Id);

                    // Create and store new lock information
                    // TODO: In a real implementation the lock should be stored in a persisted and shared system.
                    Locks[requestData.Id] = new LockInfo() { DateCreated = DateTime.UtcNow, Lock = newLock };

                    context.Response.AddHeader(WopiHeaders.ItemVersion, GetFileVersion(requestData.FullPath));
                    // Return success
                    ReturnSuccess(context.Response);
                }
            }
        }

        /// <summary>
        /// Processes a RefreshLock request
        /// </summary>
        /// <remarks>
        /// For full documentation on RefreshLock, see https://wopi.readthedocs.org/en/latest/wopi/files/RefreshLock.html
        /// </remarks>
        private void HandleRefreshLockRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (!File.Exists(requestData.FullPath))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];

            lock (Locks)
            {
                LockInfo existingLock;
                if (TryGetLock(requestData.Id, out existingLock))
                {
                    if (existingLock.Lock == newLock)
                    {
                        // There is a valid lock on the file and the existing lock matches the provided one

                        // Extend the lock timeout
                        existingLock.DateCreated = DateTime.UtcNow;
                        ReturnSuccess(context.Response);
                    }
                    else
                    {
                        // The existing lock doesn't match the requested one.  Return a lock mismatch error
                        // along with the current lock
                        ReturnLockMismatch(context.Response, ClusterLockString(existingLock.Lock));
                    }
                }
                else
                {
                    // The requested lock does not exist.  That's also a lock mismatch error.
                    ReturnLockMismatch(context.Response, reason: "File not locked");
                }
            }
        }

        /// <summary>
        /// Processes a Unlock request
        /// </summary>
        /// <remarks>
        /// For full documentation on Unlock, see https://wopi.readthedocs.org/en/latest/wopi/files/Unlock.html
        /// </remarks>
        private void HandleUnlockRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (!File.Exists(requestData.FullPath))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];

            lock (Locks)
            {
                LockInfo existingLock;
                if (TryGetLock(requestData.Id, out existingLock))
                {
                    if (existingLock.Lock == newLock)
                    {
                        // There is a valid lock on the file and the existing lock matches the provided one

                        // Remove the current lock
                        Locks.Remove(requestData.Id);
                        context.Response.AddHeader(WopiHeaders.ItemVersion, GetFileVersion(requestData.FullPath));
                        ReturnSuccess(context.Response);
                    }
                    else
                    {
                        // The existing lock doesn't match the requested one.  Return a lock mismatch error
                        // along with the current lock

                        ReturnLockMismatch(context.Response, ClusterLockString(existingLock.Lock));

                    }
                }
                else
                {
                    // The requested lock does not exist.  That's also a lock mismatch error.
                    ReturnLockMismatch(context.Response, reason: "File not locked");
                }
            }
        }

        /// <summary>
        /// Processes a UnlockAndRelock request
        /// </summary>
        /// <remarks>
        /// For full documentation on UnlockAndRelock, see https://wopi.readthedocs.org/en/latest/wopi/files/UnlockAndRelock.html
        /// </remarks>
        private void HandleUnlockAndRelockRequest(HttpContext context, WopiRequest requestData)
        {
            if (!ValidateAccess(requestData, writeAccessRequired: true))
            {
                ReturnInvalidToken(context.Response);
                return;
            }

            if (!File.Exists(requestData.FullPath))
            {
                ReturnFileUnknown(context.Response);
                return;
            }

            string newLock = context.Request.Headers[WopiHeaders.Lock];
            string oldLock = context.Request.Headers[WopiHeaders.OldLock];

            lock (Locks)
            {
                LockInfo existingLock;
                if (TryGetLock(requestData.Id, out existingLock))
                {
                    if (existingLock.Lock == oldLock)
                    {
                        // There is a valid lock on the file and the existing lock matches the provided one

                        // Replace the existing lock with the new one
                        Locks[requestData.Id] = new LockInfo() { DateCreated = DateTime.UtcNow, Lock = newLock };
                        context.Response.Headers[WopiHeaders.OldLock] = newLock;
                        ReturnSuccess(context.Response);
                    }
                    else
                    {
                        // The existing lock doesn't match the requested one.  Return a lock mismatch error
                        // along with the current lock

                        ReturnLockMismatch(context.Response, ClusterLockString(existingLock.Lock));

                    }
                }
                else
                {
                    // The requested lock does not exist.  That's also a lock mismatch error.
                    ReturnLockMismatch(context.Response, reason: "File not locked");
                }
            }
        }

        #endregion

        /// <summary>
        /// Validate WOPI ProofKey to make sure request came from the expected Office Web Apps Server.
        /// </summary>
        /// <param name="request">Request information</param>
        /// <returns>true when WOPI ProofKey validation succeeded, false otherwise.</returns>
        private static bool ValidateWopiProofKey(HttpRequest request)
        {
            // TODO: WOPI proof key validation is not implemented in this sample.
            // For more details on proof keys, see the documentation
            // https://wopi.readthedocs.org/en/latest/scenarios/proofkeys.html

            // The proof keys are returned by WOPI Discovery. For more details, see
            // https://wopi.readthedocs.org/en/latest/discovery.html

            return true;
        }

        /// <summary>
        /// Validate that the provided access token is valid to get access to requested resource.
        /// </summary>
        /// <param name="requestData">Request information, including requested file Id</param>
        /// <param name="writeAccessRequired">Whether write permission is requested or not.</param>
        /// <returns>true when access token is correct and user has access to document, false otherwise.</returns>
        private static bool ValidateAccess(WopiRequest requestData, bool writeAccessRequired)
        {
            // TODO: Access token validation is not implemented in this sample.
            // For more details on access tokens, see the documentation
            // https://wopi.readthedocs.org/en/latest/concepts.html#term-access-token
            // "INVALID" is used by the WOPIValidator.
            return !String.IsNullOrWhiteSpace(requestData.AccessToken) && (requestData.AccessToken != "INVALID");
        }

        private static void ReturnSuccess(HttpResponse response)
        {
            ReturnStatus(response, 200, "Success");
        }

        private static void ReturnInvalidToken(HttpResponse response)
        {
            ReturnStatus(response, 401, "Invalid Token");
        }

        private static void ReturnFileUnknown(HttpResponse response)
        {
            ReturnStatus(response, 404, "File Unknown/User Unauthorized");
        }

        private static void ReturnLockMismatch(HttpResponse response, string existingLock = null, string reason = null)
        {
            response.Headers[WopiHeaders.Lock] = existingLock ?? String.Empty;
            if (!String.IsNullOrEmpty(reason))
            {
                response.Headers[WopiHeaders.LockFailureReason] = reason;
            }

            ReturnStatus(response, 409, "Lock mismatch/Locked by another interface");
        }

        private static void ReturnServerError(HttpResponse response)
        {
            ReturnStatus(response, 500, "Server Error");
        }

        private static void ReturnUnsupported(HttpResponse response)
        {
            ReturnStatus(response, 501, "Unsupported");
        }

        private static void ReturnStatus(HttpResponse response, int code, string description)
        {
            response.StatusCode = code;
            response.StatusDescription = description;
        }

        private bool TryGetLock(string fileId, out LockInfo lockInfo)
        {
            // TODO: This lock implementation is not thread safe and not persisted and all in all just an example.
            if (Locks.TryGetValue(fileId, out lockInfo))
            {
                if (lockInfo.Expired)
                {
                    Locks.Remove(fileId);
                    return false;
                }
                return true;
            }

            return false;
        }
    }
}
