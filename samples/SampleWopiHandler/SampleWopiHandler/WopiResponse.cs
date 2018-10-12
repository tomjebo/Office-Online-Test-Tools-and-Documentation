
namespace SampleWopiHandler
{
	class CheckFileInfoResponse
	{
		// This is a subset of all CheckFileInfo properties.
		// Use optional properties in accordance with the [MS-WOPI] Web Application Open Platform Interface Protocol specification.

		public string BaseFileName { get; set; }
		public string OwnerId { get; set; }
		public int Size { get; set; }
		public string Version { get; set; }

		public bool AllowExternalMarketplace { get; set; }
		public string BreadcrumbBrandName { get; set; }
		public string BreadcrumbBrandUrl { get; set; }
		public string BreadcrumbFolderName { get; set; }
		public string BreadcrumbFolderUrl { get; set; }
		public string BreadcrumbDocName { get; set; }
		public string BreadcrumbDocUrl { get; set; }
		public string ClientUrl { get; set; }

		public bool UserCanWrite { get; set; }
		public bool WebEditingDisabled { get; set; }
		public bool RestrictedWebViewOnly { get; set; }
		public bool SupportsSecureStore { get; set; }
		public bool SupportsFolders { get; set; }
		public bool SupportsScenarioLinks { get; set; }
		public bool SupportsCoauth { get; set; }
		public bool ReadOnly { get; set; }
		public bool SupportsLocks { get; set; }
		public bool SupportsEditing { get; set; }
		public bool SupportsUpdate { get; set; }
		public bool SupportsCobalt { get; set; }
		public bool ClosePostMessage { get; set; }
		public bool CloseButtonClosesWindow { get; set; }
		public bool UserCanNotWriteRelative { get; set; }
		public string HostEditUrl { get; set; }
		public string HostViewUrl { get; set; }
		public string PostMessageOrigin { get; set; }
		public string CloseUrl { get; set; }
		public string DownloadUrl { get; set; }

		public string UserFriendlyName { get; set; }
		public string SHA256 { get; set; }
	}

	class PutRelativeFileResponse
	{
		public string Name { get; set; }
		public string Url { get; set; }
		public string HostViewUrl { get; set; }
		public string HostEditUrl { get; set; }

		public const string samplehostfile_first = @"<!doctype html> <html> <head> <meta charset=""utf-8""> <!-- Enable IE Standards mode --> <meta http-equiv=""x-ua-compatible"" content=""ie=edge""> <title></title> <meta name=""description"" content=""""> <meta name=""viewport"" content=""width=device-width, initial-scale=1, maximum-scale=1, minimum-scale=1, user-scalable=no""> <script src=""http://ajax.aspnetcdn.com/ajax/jQuery/jquery-1.12.0.min.js""></script> <link rel=""shortcut icon"" href=""http://jeebsoossvr/wv/resources/1033/FavIcon_Word.ico"" /> <style type=""text/css""> body { margin: 0; padding: 0; overflow: hidden; -ms-content-zooming: none; } #office_frame { width: 100%; height: 100%; position: absolute; top: 0; left: 0; right: 0; bottom: 0; margin: 0; border: none; display: block; } /* The Modal (background) */ .modal { display: none; /* Hidden by default */ position: fixed; /* Stay in place */ z-index: 1; /* Sit on top */ left: 0; top: 0; width: 100%; /* Full width */ height: 100%; /* Full height */ overflow: auto; /* Enable scroll if needed */ background-color: rgb(0,0,0); /* Fallback color */ background-color: rgba(0,0,0,0.4); /* Black w/ opacity */ } /* Modal Content/Box */ .modal-content { background-color: #fefefe; margin: 15% auto; /* 15% from the top and centered */ padding: 20px; border: 1px solid #888; width: 80%; /* Could be more or less, depending on screen size */ } /* The Close Button */ .close { color: #aaa; float: right; font-size: 28px; font-weight: bold; } .close:hover, .close:focus { color: black; text-decoration: none; cursor: pointer; } </style> </head> <body> <div> <h1>Tom's WOPI PutRelativeFile Test Page Host: jeebsoossvr</h1> <p> Use this page to test the WOPI host implementation based on: <a href=""https://github.com/Microsoft/Office-Online-Test-Tools-and-Documentation"">Office-Online-Test-Tools-and-Documentation</a> </p> <button class=""wopi_vw_button"" id=""vwDoc"">View Word Document</button> </div> <div> <form id=""office_form_vwDoc"" name=""office_form"" target=""office_frame""action=""http://jeebsoossvr/we/wordeditorframe.aspx?WOPISrc=http%3A%2F%2Fjeebshpzb:8888%2Fwopi%2Ffiles%2F";
		public const string samplehostfile_second = @"&access_token=DEADBEEFDEADBEEFDEADBEEF&access_token_ttl=0"" method=""post""><input name=""WOPIsrc"" value=""http%3A%2F%2Fjeebshpzb:8888%2Fwopi%2Ffiles%2F";
        public const string samplehostfile_third = @""" type=""hidden"" /><input name=""access_token"" value=""DEADBEEFDEADBEEFDEADBEEF"" type=""hidden"" /> <input name=""access_token_ttl"" value=""0"" type=""hidden"" /> </form><span id=""frameholder""></span><script type=""text/javascript""> window.addEventListener('message', receiver, false); function receiver(e) { /* The actual message is contained in the data property of the event. */  var msg = JSON.parse(e.data); alert(msg.MessageId); /* The message ID is now a property of the message object. */  var msgId = msg.MessageId; /* The message parameters themselves are in the Values */ /* parameter on the message object. */  var msgData = msg.Values; console.log(msg); /* Do something with the message here. */  if (msgId === ""UI_Close"") { console.log(""Got a UI_Close, navigating back to the host page.""); window.location.href = ""http://localhost/Closed.html""; } } /* original button handler... */  $('.wopi_vw_button').click(function (event) { var frameholder = document.getElementById('frameholder'); var office_frame = document.createElement('iframe'); office_frame.name = 'office_frame'; office_frame.id = 'office_frame'; /* This attribute allows true fullscreen mode in slideshow view */ /* when using PowerPoint Online's 'view' action. */ office_frame.setAttribute('allowfullscreen', 'true'); frameholder.appendChild(office_frame); document.getElementById('office_form_' + event.target.id).submit(); }); </script> </div> </body> </html>";
	}
}
