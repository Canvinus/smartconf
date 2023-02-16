using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ezmeetsAPI.Models;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;
using System.IO;

namespace ezmeetsAPI.Controllers
{
    [Authorize]
    [Route("v1/[controller]")]
    [ApiController]
    public class MeetingsController : ControllerBase
    {
        private readonly MyDBContext _context;
        private readonly IConfiguration _configuration;
        private readonly string _jitsiHost;
        private readonly string _photosPath;
        private readonly string _baseDir;
        private readonly string _host;
        private readonly UserManager<User> _userManager;
        public MeetingsController(MyDBContext context, IConfiguration configuration, UserManager<User> userManager)
        {
            _context = context;
            _configuration = configuration;
            _userManager = userManager;

            _jitsiHost = _configuration["JitsiServer_Host"];
            _host = _configuration["API_Host"];

            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var staticPath = Path.Combine(_baseDir, "static/");
            _photosPath = Path.Combine(staticPath, "Photos/");

            CreateDir(_photosPath);
        }

        private async Task<User> CurrentUser()
        {
            var claimsIdentity = User.Identity as ClaimsIdentity;
            var userID = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userID);

            return user;
        }

        [Authorize]
        [HttpGet(template: "TimeNow")]
        public string TimeNow()
        {
            return DateTime.Now.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss");
        }

        /// <summary>
        /// Получает список всех созданных встреч
        /// </summary>
        /// <returns>Meeting[]</returns>
        /// <response code="200">Возвращает список</response>
        [AdminAuthorize]
        [HttpGet(template: "GetAll")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<Meeting>>> GetAll([FromQuery] bool all, [FromQuery] bool isLive, [FromQuery] bool hasEnded)
        {
            var currentUser = await CurrentUser();

            if (currentUser == null)
            {
                return NotFound();
            }

            var userRoles = await _userManager.GetRolesAsync(currentUser);

            var allMeetings = await _context.Meetings
                    .Include(m => m.AllowedUsers)
                    .Include(m => m.UsersAtMeeting).ThenInclude(u => u.ConnectionLogs)
                    .Include(m => m.UsersAtMeeting).ThenInclude(u => u.CamStatuses).ThenInclude(cs => cs.CamStatusesData)
                    .ToListAsync();

            if (userRoles.FirstOrDefault() == UserRoles.SuperAdmin) 
            { 
                allMeetings = allMeetings
                    .ToList();

                if(all)
                    return allMeetings;
                else
                    return allMeetings
                        .Where(m => m.isLive == isLive && m.hasEnded == hasEnded)
                        .ToList();
            }
            else
            {
                allMeetings = allMeetings
                    .Where(m => m.AllowedUsers.Any(au => au.UserID == currentUser.Id))
                    .ToList();

                if(all)
                    return allMeetings;
                else
                    return allMeetings
                    .Where(m => m.isLive == isLive && m.hasEnded == hasEnded)
                    .ToList();
            }
        }

        /// <summary>
        /// Получает встречу
        /// </summary>
        /// <param name="meetingID">ID встречи</param>
        /// <returns>Meeting[]</returns>
        /// <response code="200">Возвращает встречу</response>
        /// <response code="404">Встречи не существует</response>
        [AdminAuthorize]
        [HttpGet(template: "Get")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Meeting>> Get([FromQuery] int meetingID)
        {
            var meeting = await _context.Meetings
                                            .Include(m => m.AllowedUsers)
                                            .Include(m => m.UsersAtMeeting).ThenInclude(u => u.ConnectionLogs)
                                            .Include(m => m.UsersAtMeeting).ThenInclude(u => u.CamStatuses).ThenInclude(cs => cs.CamStatusesData)
                                            .FirstOrDefaultAsync(m => m.MeetingID == meetingID);

            if (meeting == null)
            {
                return NotFound();
            }

            return meeting;
        }


        /// <summary>
        /// Получает список всех созданных встреч
        /// </summary>
        /// <returns>Meeting[]</returns>
        /// <response code="200">Возвращает список</response>
        [AdminAuthorize]
        [HttpGet(template: "GetScheduled")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<Meeting>>> GetScheduled()
        {
            var meetings = await _context.Meetings
                                    .Include(m => m.AllowedUsers)
                                    .Include(m => m.UsersAtMeeting).ThenInclude(u => u.ConnectionLogs)
                                    .Include(m => m.UsersAtMeeting).ThenInclude(u => u.CamStatuses).ThenInclude(cs => cs.CamStatusesData)
                                    .ToListAsync();
            return meetings
                .Where(m => !m.isLive)
                .ToList();
        }

        [AdminAuthorize]
        [HttpGet(template: "GetLive")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<Meeting>>> GetLive()
        {
            var meetings = await _context.Meetings
                                    .Include(m => m.AllowedUsers)
                                    .Include(m => m.UsersAtMeeting).ThenInclude(u => u.ConnectionLogs)
                                    .Include(m => m.UsersAtMeeting).ThenInclude(u => u.CamStatuses).ThenInclude(cs => cs.CamStatusesData)
                                    .ToListAsync();

            return meetings
                .Where(m => m.isLive)
                .ToList();
        }

        [Authorize]
        [HttpGet(template: "CurrentUserMeetings")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IEnumerable<Meeting>> CurrentUserMeetings([FromQuery] bool all, [FromQuery] bool isLive, [FromQuery] bool hasEnded)
        {
            var currentUser = await CurrentUser();

            var allmeetings = await _context.Meetings
                                    .Include(m => m.AllowedUsers)
                                    .Include(m => m.UsersAtMeeting).ThenInclude(u => u.ConnectionLogs)
                                    .Include(m => m.UsersAtMeeting).ThenInclude(u => u.CamStatuses).ThenInclude(cs => cs.CamStatusesData)
                                    .ToListAsync();

            allmeetings = allmeetings
                    .Where(m => m.AllowedUsers.Any(au => au.UserID == currentUser.Id))
                    .ToList();

            if(all)
                return allmeetings;
            else
                return allmeetings
                    .Where(m => m.isLive == isLive && m.hasEnded == hasEnded)
                    .ToList();
        }

        [AllowAnonymous]
        [HttpPost(template:"Log")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<RoomLog>> Log([FromBody] RoomLog roomLog)
        {
            if(roomLog._nick == "Bot")
            {
                return BadRequest();
            }

            var meeting = await _context.Meetings.FirstOrDefaultAsync(m => m.Name.Replace(" ", "") == roomLog._room);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.FullName == roomLog._nick);
            var userAtMeeting = await _context.UsersAtMeeting.FirstOrDefaultAsync(u => u.UserID == user.Id && u.Meeting == meeting);

            if (userAtMeeting == null)
            {
                var userRoles = await _userManager.GetRolesAsync(user);
                var userRole = userRoles.FirstOrDefault();

                meeting.UsersAtMeeting.Add(

                    new UserAtMeeting { 
                        UserID = user.Id, 
                        FullName = user.FullName, 
                        UserName = user.UserName,
                        Groups = user.Groups, 
                        Role = userRole, 
                        AvatarPath = user.AvatarPath, 
                        Meeting = meeting});

                await _context.SaveChangesAsync();

                userAtMeeting = await _context.UsersAtMeeting.FirstOrDefaultAsync(u => u.UserID == user.Id && u.Meeting == meeting);
            }

            userAtMeeting.ConnectionLogs.Add(new ConnectionLog { DateTime = DateTime.Now, Action = roomLog._action});
            
            if(roomLog._action == "enter")
            {
                userAtMeeting.OnlineStatus = "Online";
            }
            else if(roomLog._action == "leave")
            {
                userAtMeeting.OnlineStatus = "Offline";
            }

            await _context.SaveChangesAsync();

            return CreatedAtAction("Log", roomLog);
        }

        private async Task<ActionResult> CreateDir(string dirName)
        {
            if (Directory.Exists(dirName))
                return StatusCode(StatusCodes.Status200OK);

            try
            {
                await Task.Run(() => Directory.CreateDirectory(dirName));
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            return StatusCode(StatusCodes.Status201Created);
        }

        [AdminAuthorize]
        [HttpPost(template: "AddCamStatus")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<Object>> AddCamStatus([FromQuery] int meetingID,[FromQuery] string userID,[FromQuery] string status, [FromForm] string json, [FromForm] IFormFile file)
        {
            List<CamStatusData> camStatusesData = JsonConvert.DeserializeObject<List<CamStatusData>>(json);
            var meeting = await _context.Meetings
                                .Include(m => m.AllowedUsers)
                                .Include(m => m.UsersAtMeeting).ThenInclude(u => u.ConnectionLogs)
                                .Include(m => m.UsersAtMeeting).ThenInclude(u => u.CamStatuses).ThenInclude(cs => cs.CamStatusesData)
                                .FirstOrDefaultAsync(m => m.MeetingID == meetingID);

            var user = meeting.UsersAtMeeting.FirstOrDefault(u => u.UserID == userID);

            if (user == null || meeting == null)
            {
                return NotFound();
            }
            
            int userIndex = meeting.UsersAtMeeting.IndexOf(user);

            var mPath = Path.Combine(Path.Combine(_photosPath), meetingID.ToString() + "/");
            var mDirResult = await CreateDir(mPath);
            if (mDirResult == StatusCode(500))
                return mDirResult;

            var umPath = Path.Combine(mPath, userID.ToString() + "/");
            var umDirResult = await CreateDir(umPath);
            if (umDirResult == StatusCode(500))
                return umDirResult;

            var fumPath = Path.Combine(umPath, file.FileName);
            try
            {
                using (var stream = new FileStream(fumPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            meeting.UsersAtMeeting[userIndex].CamStatuses.Add(
                
                new CamStatus{
                    PhotoPath = fumPath.Replace(_baseDir, _host),
                    Status = status, 
                    CamStatusesData = camStatusesData,
                    DateTime = DateTime.Now});

            _context.Meetings.Update(meeting);
            await _context.SaveChangesAsync();

            return Ok();
        }

        private string GetToken(string id, string name, string email, string avatar, string roomName, bool moderator, int duration)
        {
            string secret = _configuration["JWT:Secret"];

            var epoch = DateTimeOffset.Now.AddMinutes(duration).ToUnixTimeSeconds();

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            var credentials = new SigningCredentials(securityKey, "HS256");

            var header = new JwtHeader(credentials);

            JwtPayload payload = new JwtPayload();

            payload.AddClaim(new Claim("context", "{'user': { 'id': '" + id + "', 'name': '" + name + "', 'email': '" + email + "' }}", JsonClaimValueTypes.Json));
            payload.AddClaim(new Claim("moderator", moderator.ToString().ToLower(), ClaimValueTypes.Boolean));
            payload.AddClaim(new Claim("aud", _configuration["JWT:ValidAudience"]));
            payload.AddClaim(new Claim("iss", _configuration["JWT:ValidIssuer"]));
            payload.AddClaim(new Claim("sub", _configuration["JWT:ValidIssuer"]));
            payload.AddClaim(new Claim("room", roomName));
            payload.AddClaim(new Claim("exp", epoch.ToString(), ClaimValueTypes.Integer64));

            var secToken = new JwtSecurityToken(header, payload);
            var handler = new JwtSecurityTokenHandler();

            var tokenString = handler.WriteToken(secToken);

            return tokenString;
        }

        [AdminAuthorize]
        [HttpPost(template: "ScheduleMeeting")]
        public async Task<ActionResult<Meeting>> ScheduleMeeting([FromBody] ScheduleMeetingModel model)
        {
            var currentUser = await CurrentUser();

            if (currentUser == null)
            {
                return NotFound();
            }

            var meeting = await _context.Meetings.FirstOrDefaultAsync(m => m.Name == model.Name);

            if (meeting != null)
                return StatusCode(StatusCodes.Status500InternalServerError,
                        new Response { Status = "Error", Message = "Meeting with the same name already exists"});

            var allowedUsers = model.AllowedUsers.Distinct().ToList();

            meeting = new Meeting { 
                Name = model.Name, 
                StartTime = model.StartTime,
                Duration = model.Duration,
                EndingTime = model.StartTime.AddMinutes(model.Duration),
                AllowedUsers = allowedUsers, 
                Description = model.Description};

            meeting.AllowedUsers.Add(new AllowedUser{ UserID = currentUser.Id});

            _context.Meetings.Add(meeting);
            await _context.SaveChangesAsync();

            return CreatedAtAction("ScheduleMeeting", new { id = meeting.MeetingID }, meeting);
        }

        [AdminAuthorize]
        [HttpPut(template: "UpdateScheduledMeeting")]
        public async Task<ActionResult<Meeting>> UpdateScheduledMeeting([FromBody] UpdateMeetingModel model)
        {
            var meetingToUpdate = await _context.Meetings
                    .Include(m => m.AllowedUsers)
                    .Include(m => m.UsersAtMeeting).ThenInclude(u => u.ConnectionLogs)
                    .Include(m => m.UsersAtMeeting).ThenInclude(u => u.CamStatuses).ThenInclude(cs => cs.CamStatusesData)
                    .FirstOrDefaultAsync(m => m.MeetingID == model.MeetingID);

            if (meetingToUpdate.isLive)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new Response { Status = "Error", Message = "The meeting has started" });
            }

            if (meetingToUpdate.hasEnded)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new Response { Status = "Error", Message = "The meeting has ended" });
            }
            

            meetingToUpdate.Name = model.Name;
            meetingToUpdate.Description = model.Description;
            meetingToUpdate.StartTime = model.StartTime;
            meetingToUpdate.Duration = model.Duration;
            meetingToUpdate.EndingTime = model.StartTime.AddMinutes(model.Duration);

            var allowedUsers = model.AllowedUsers.Distinct().ToList();

            foreach(var allowedUser in meetingToUpdate.AllowedUsers) 
            {
                _context.AllowedUsers.Remove(allowedUser);
            }

            meetingToUpdate.AllowedUsers = allowedUsers;

            _context.Meetings.Update(meetingToUpdate);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [AdminAuthorize]
        [HttpDelete(template: "DeleteScheduledMeeting")]
        public async Task<ActionResult<Meeting>> DeleteScheduledMeeting([FromQuery] int meetingID)
        {
            var meetings = await _context.Meetings
                                .Include(m => m.AllowedUsers)
                                .Include(m => m.UsersAtMeeting).ThenInclude(u => u.ConnectionLogs)
                                .Include(m => m.UsersAtMeeting).ThenInclude(u => u.CamStatuses).ThenInclude(cs => cs.CamStatusesData)
                                .Include(m => m.Photos)
                                .ToListAsync();

            var meetingToDelete = meetings
                                .FirstOrDefault(m => m.MeetingID == meetingID);

            if (meetingToDelete.isLive)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new Response { Status = "Error", Message = "The meeting is live" });
            }

            if (meetingToDelete.hasEnded)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new Response { Status = "Error", Message = "The meeting has ended" });
            }

            foreach (var allowedUser in meetingToDelete.AllowedUsers)
                _context.AllowedUsers.Remove(allowedUser);

            foreach (var userAtMeeting in meetingToDelete.UsersAtMeeting) 
            {
                foreach(var connectionLog in userAtMeeting.ConnectionLogs)
                    _context.ConnectionLogs.Remove(connectionLog);

                foreach (var camStatus in userAtMeeting.CamStatuses)
                {
                    foreach(var camStatusData in camStatus.CamStatusesData)
                        _context.CamStatusesData.Remove(camStatusData);
                    _context.CamStatuses.Remove(camStatus);
                }

                _context.UsersAtMeeting.Remove(userAtMeeting);
            }

            foreach (var photo in meetingToDelete.Photos)
                _context.Photos.Remove(photo);

            _context.Meetings.Remove(meetingToDelete);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [AdminAuthorize]
        [HttpPost(template: "EndMeeting")]
        public async Task<ActionResult<Meeting>> EndMeeting([FromQuery] int meetingID)
        {
            var meeting = await _context.Meetings
                .FirstOrDefaultAsync(m => m.MeetingID == meetingID);

            if (meeting == null)
            {
                return NotFound();
            }

            meeting.EndingTime = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Выдает ссылку на подключение пользователя к видеовстречи через генерацию JWT - токена
        /// </summary>
        /// <param name="meetingID">ID встречи</param>
        /// <returns></returns>
        /// <remarks>Если встречи не было, то она создается</remarks>
        /// <response code="200">Возвращает ссылку для подключения к конференции</response>
        /// <response code="404">Пользователь не найден</response>
        [Authorize]
        [HttpPost(template: "Join")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<Object> Join([FromQuery] int meetingID)
        {
            var currentUser = await CurrentUser();

            if(currentUser == null)
            {
                return NotFound();
            }

            var meeting = await _context.Meetings
                .Include(m => m.AllowedUsers)
                .FirstOrDefaultAsync(m => m.MeetingID == meetingID);

            if (meeting == null)
            {
                return NotFound();
            }

            var userRoles = await _userManager.GetRolesAsync(currentUser);
            bool moderator = true;

            if (DateTime.Now < meeting.StartTime)
                return StatusCode(StatusCodes.Status500InternalServerError,
                        new Response { Status = "Error", Message = "Meeting has not started yet" });

            if (meeting.hasEnded)
                return StatusCode(StatusCodes.Status500InternalServerError,
                        new Response { Status = "Error", Message = "Meeting has ended" });

            if (userRoles.FirstOrDefault() != UserRoles.SuperAdmin) 
            {
                if (currentUser.ApproveStatus != ApproveStatuses.Approved)
                    return StatusCode(StatusCodes.Status500InternalServerError,
                                new Response { Status = "Error", Message = "You are not approved" });

                if (!meeting.AllowedUsers.Any(au => au.UserID == currentUser.Id))
                    return StatusCode(StatusCodes.Status500InternalServerError,
                            new Response { Status = "Error", Message = "You are not allowed at that meeting" });

                moderator = false;
                if (userRoles.FirstOrDefault() != UserRoles.User)
                    moderator = true;
            }

            var currentUserMeetings = await CurrentUserMeetings(false, true, false);

            bool currentUserOnlineStatus = false;
            foreach(var mtng in currentUserMeetings)
                foreach(var user in mtng.UsersAtMeeting)
                {
                    if(user.UserID == currentUser.Id)
                        if (user.OnlineStatus == "Online")
                        {
                            currentUserOnlineStatus = true;
                        }

                }

            if (currentUserOnlineStatus)
                return StatusCode(StatusCodes.Status500InternalServerError,
                            new Response { Status = "Error", Message = "Already on meeting" });

            var jitsiMeetingNameFormat = meeting.Name.Replace(" ", "");

            return _jitsiHost + jitsiMeetingNameFormat + "?jwt=" + GetToken(currentUser.Id, currentUser.FullName, currentUser.Email, currentUser.AvatarPath, jitsiMeetingNameFormat, moderator, meeting.Duration) + "#config.startWithAudioMuted=true&config.startWithVideoMuted=true";
        }
    }
}
