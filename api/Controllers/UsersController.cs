using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ezmeetsAPI.Models;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using System.Net.Http;

namespace ezmeetsAPI.Controllers
{
    [Authorize]
    [Route("v1/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly string _staticPath = AppDomain.CurrentDomain.BaseDirectory + "/static/";
        private readonly string _host;
        private readonly string _bots_host;
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly MyDBContext _context;
        private readonly IConfiguration _configuration;
        private readonly GroupsController _groups;

        public UsersController(UserManager<User> userManager, RoleManager<IdentityRole> roleManager, MyDBContext context, IConfiguration configuration)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _configuration = configuration;

            _host = _configuration["API_Host"];
            _bots_host = _configuration["Bots_Host"];
            _groups = new GroupsController(_userManager, _context, _configuration);
        }

        [Authorize]
        [HttpGet(template:"CurrentUser")]
        public async Task<User> CurrentUser()
        {
            var claimsIdentity = User.Identity as ClaimsIdentity;
            var userID = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userID);
            user.Groups = (await _groups.GetByUser(user.Id)).ToList();

            return user;
        }

        [Authorize]
        [HttpGet(template: "CurrentUserRole")]
        public async Task<string> CurrentUserRole()
        {
            var claimsIdentity = User.Identity as ClaimsIdentity;
            var userID = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userID);

            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);

            return currentUserRoles.FirstOrDefault();
        }

        [AdminAuthorize]
        [HttpGet(template: "GetUserRole")]
        public async Task<Object> GetUserRole([FromQuery] string userID)
        {
            var user = await _context.Users.FindAsync(userID);

            if (user == null)
            {
                return NotFound();
            }

            var userRoles = await _userManager.GetRolesAsync(user);

            return userRoles.FirstOrDefault();
        }

        [Authorize]
        [HttpPost(template: "ChangePassword")]
        public async Task<IActionResult> ChangePassword([FromQuery]string currentPassword, [FromQuery]string newPassword)
        {
            var currentUser = await CurrentUser();

            var result = await _userManager.ChangePasswordAsync(currentUser, currentPassword, newPassword);

            if (!result.Succeeded)
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new Response { Status = "Error", Message = result.Errors.FirstOrDefault().Description });

            return Ok(new Response { Status = "Success", Message = "Password changed successfully!" });
        }

        [Authorize]
        [HttpPost(template: "ChangeFullName")]
        public async Task<IActionResult> ChangeFullName([FromQuery]string newFullName)
        {
            var currentUser = await CurrentUser();

            currentUser.FullName = newFullName;

            _context.Users.Update(currentUser);
            await _context.SaveChangesAsync();

            return Ok(new Response { Status = "Success", Message = "FullName changed successfully!" });
        }

        [Authorize]
        [HttpPost(template: "CheckFace")]
        public async Task<ActionResult> CheckFace([FromForm] IFormFile file)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    MultipartFormDataContent content = new MultipartFormDataContent();
                    content.Add(new StreamContent(file.OpenReadStream()), "file", file.FileName);

                    HttpResponseMessage response = await client.PostAsync(_bots_host + "checkFace", content);

                    return Ok(await response.Content.ReadAsStringAsync());
                }
            }
            catch
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [Authorize]
        [HttpPost(template: "CompareFaces")]
        public async Task<ActionResult> CompareFaces([FromForm] IFormFile file1, IFormFile file2)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    MultipartFormDataContent content = new MultipartFormDataContent();
                    content.Add(new StreamContent(file1.OpenReadStream()), "file1", file1.FileName);
                    content.Add(new StreamContent(file2.OpenReadStream()), "file2", file2.FileName);

                    HttpResponseMessage response = await client.PostAsync(_bots_host + "compareFaces", content);

                    return Ok(await response.Content.ReadAsStringAsync());
                }
            }
            catch
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Получает список всех пользователей
        /// </summary>
        /// <returns></returns>
        /// <response code="200">Возвращает список</response>
        [AdminAuthorize]
        [HttpGet(template: "GetAll")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<User>>> GetAll()
        {
            var users = await _context.Users.ToListAsync();

            for(int i = 0; i < users.Count; i++)
            {
                users[i].Groups = (await _groups.GetByUser(users[i].Id)).ToList();
            }

            return users;
        }

        [AdminAuthorize]
        [HttpGet(template: "GetAllAllowed")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<User>>> GetAllAllowed()
        {
            var users = await _context.Users.ToListAsync();

            for (int i = 0; i < users.Count; i++)
            {
                users[i].Groups = (await _groups.GetByUser(users[i].Id)).ToList();
            }

            return users
                .Where(u => u.ApproveStatus == ApproveStatuses.Approved)
                .ToList();
        }

        [AllowAnonymous]
        [HttpPost(template: "CheckUserName")]
        public async Task<ActionResult> CheckUserName(string userName)
        {
            var users = (await GetAll()).Value;

            if (!users.Any(u => u.NormalizedUserName == userName.ToUpper()))
                return Ok(new Response { Status = "Success", Message = "The username is free" });
            else
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new Response { Status = "Error", Message = "The username is taken" });
        }

        /// <summary>
        /// Получает пользователя
        /// </summary>
        /// <param name="userID">ID пользователя</param>
        /// <returns></returns>
        /// <response code="200">Возвращает пользователя</response>
        /// <response code="404">Пользователя не существует</response>
        [AdminAuthorize]
        [HttpGet(template:"Get")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<User>> Get([FromQuery] string userID)
        {
            var user = await _context.Users.FindAsync(userID);

            if (user == null)
            {
                return NotFound();
            }
            
            user.Groups = (await _groups.GetByUser(user.Id)).ToList();

            return user;
        }

        [AllowAnonymous]
        [HttpPost(template:"Login")]
        public async Task<ActionResult> Login([FromBody] LoginModel model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user != null && await _userManager.CheckPasswordAsync(user, model.Password))
            {
                var userRoles = await _userManager.GetRolesAsync(user);

                var authClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                };

                foreach (var userRole in userRoles)
                {
                    authClaims.Add(new Claim(ClaimTypes.Role, userRole));
                }

                var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JWT:Secret"]));

                var token = new JwtSecurityToken(
                    issuer: _configuration["JWT:ValidIssuer"],
                    audience: _configuration["JWT:ValidAudience"],
                    expires: DateTime.Now.AddHours(3),
                    claims: authClaims,
                    signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
                    );

                return Ok(new
                {
                    token = new JwtSecurityTokenHandler().WriteToken(token),
                    expiration = token.ValidTo
                });
            }
            return Unauthorized();
        }

        [AdminAuthorize]
        [HttpPost(template: "ToApprove")]
        public async Task<ActionResult<User>> ToApprove([FromQuery] string userID, bool yes)
        {
            var currentUser = await CurrentUser();

            if (currentUser == null)
            {
                return NotFound();
            }

            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);

            var userToApprove = await _context.Users
                .Include(u => u.Photos)
                .Include(m => m.UserAtMeetings).ThenInclude(u => u.ConnectionLogs)
                .Include(m => m.UserAtMeetings).ThenInclude(u => u.CamStatuses)
                .FirstOrDefaultAsync(u => u.Id == userID);

            if (userToApprove == null)
            {
                return NotFound();
            }

            if(yes)
                userToApprove.ApproveStatus = ApproveStatuses.Approved;
            else
                userToApprove.ApproveStatus = ApproveStatuses.NotApproved;

            await _userManager.UpdateAsync(userToApprove);

            return Ok();
        }

        [AllowAnonymous]
        [HttpPost(template: "Register")]
        public async Task<ActionResult> Register([FromBody] RegisterModel model)
        {
            var userExists = await _userManager.FindByNameAsync(model.UserName);
            if (userExists != null)
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new Response { Status = "Error", Message = "User already exists!" });

            User user = new User()
            {
                SecurityStamp = Guid.NewGuid().ToString(),
                UserName = model.UserName,
                FullName = model.FullName,
                Email = model.Email,
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new Response { Status = "Error", Message = result.Errors.FirstOrDefault().Description });

            if (!await _roleManager.RoleExistsAsync(UserRoles.User))
                await _roleManager.CreateAsync(new IdentityRole(UserRoles.User));

            if (await _roleManager.RoleExistsAsync(UserRoles.User))
            {
                await _userManager.AddToRoleAsync(user, UserRoles.User);

                user.Role = UserRoles.User;
                await _userManager.UpdateAsync(user);
            }

            var photosPath = Path.Combine(_staticPath, "Photos/");
            var avasPath = Path.Combine(_staticPath, "Resources/Avatars/");

            try
            {
                await Task.Run(() => Directory.CreateDirectory(Path.Combine(photosPath, user.Id.ToString())));
                await Task.Run(() => Directory.CreateDirectory(Path.Combine(avasPath, user.Id.ToString())));
            }
            catch (Exception)
            {
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new Response{ Status = "Error", Message = "Error occured, while creating user's directory"});
            }
            
            var res = (OkObjectResult)Login(new LoginModel { UserName = model.UserName, Password = model.Password }).Result;

            return Ok(res.Value);
        }

        [AllowAnonymous]
        [HttpPost(template: "MakeSuperAdmin")]
        public async Task<IActionResult> MakeSuperAdmin([FromQuery] string userID, [FromQuery] string secret)
        {
            if (secret != _configuration["JWT:Secret"])
                return StatusCode(StatusCodes.Status400BadRequest, 
                    new Response { Status = "Error", Message = "Wrong secret" });

            if (!await _roleManager.RoleExistsAsync(UserRoles.SuperAdmin))
                await _roleManager.CreateAsync(new IdentityRole(UserRoles.SuperAdmin));

            var user = await _context.Users.FindAsync(userID);

            if (user == null)
            {
                return NotFound();
            }

            if (await _roleManager.RoleExistsAsync(UserRoles.SuperAdmin))
            {
                var userRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, userRoles);

                await _userManager.AddToRoleAsync(user, UserRoles.SuperAdmin);

                user.Role = UserRoles.SuperAdmin;
                await _userManager.UpdateAsync(user);
            }

            return Ok(new Response { Status = "Success", Message = "SuperAdmin role granted" });
        }

        [AdminAuthorize(Roles = UserRoles.SuperAdmin)]
        [AllowAnonymous]
        [HttpPost(template: "MakeAdmin")]
        public async Task<IActionResult> MakeAdmin([FromQuery] string userID)
        {
            if (!await _roleManager.RoleExistsAsync(UserRoles.Admin))
                await _roleManager.CreateAsync(new IdentityRole(UserRoles.Admin));

            var user = await _context.Users.FindAsync(userID);

            if (user == null)
            {
                return NotFound();
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            
            if (userRoles.FirstOrDefault() != UserRoles.User)
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new Response{Status = "Error", Message = "The user is already admin" });

            if (await _roleManager.RoleExistsAsync(UserRoles.SuperAdmin))
            {
                await _userManager.RemoveFromRolesAsync(user, userRoles);

                await _userManager.AddToRoleAsync(user, UserRoles.Admin);

                user.Role = UserRoles.Admin;
                await _userManager.UpdateAsync(user);
            }

            return Ok(new Response { Status = "Success", Message = "Admin role granted" });
        }

        /// <summary>
        /// Обновляет информацию о пользователе
        /// </summary>
        /// <param name="userToChange"></param>
        /// <returns></returns>
        /// <remarks>
        /// Здесь обязательно передаем ID!
        /// </remarks>
        /// <response code="201">Возвращает созданного пользователя</response>
        [AdminAuthorize]
        [HttpPut(template:"Update")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        public async Task<ActionResult<User>> Update([FromBody] User userToChange)
        {
            var currentUser = await CurrentUser();

            if (currentUser == null)
                return NotFound();

            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);
            var userToChangeRoles = await _userManager.GetRolesAsync(userToChange);

            if (currentUserRoles.FirstOrDefault() != UserRoles.SuperAdmin
                && userToChangeRoles.FirstOrDefault() != UserRoles.User)
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new Response { Status = "Error", Message = "You need to be SuperAdmin to update admin data" });

            _context.Users.Update(userToChange);
            await _context.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Удаляет пользователя
        /// </summary>
        /// <param name="userID">ID пользователя</param>
        /// <returns></returns>
        /// <response code="200">Удаляет пользователя</response>
        /// <response code="404">Пользователя не существует</response>
        /// <response code="500">Ошибка, при удалении папки</response>
        [AdminAuthorize]
        [HttpDelete]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<User>> Delete([FromQuery] string userID)
        {
            var currentUser = await CurrentUser();

            if (currentUser == null)
            {
                return NotFound();
            }

            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);

            var userToDelete = await _context.Users
                .Include(u => u.Photos)
                .Include(m => m.UserAtMeetings).ThenInclude(u => u.ConnectionLogs)
                .Include(m => m.UserAtMeetings).ThenInclude(u => u.CamStatuses)
                .FirstOrDefaultAsync(u => u.Id == userID);

            if (userToDelete == null)
            {
                return NotFound();
            }

            var userToDeleteRoles = await _userManager.GetRolesAsync(userToDelete);

            if (currentUserRoles.FirstOrDefault() != UserRoles.SuperAdmin
                && userToDeleteRoles.FirstOrDefault() != UserRoles.User)
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    new Response { Status = "Error", Message = "You need to be SuperAdmin to update admin data" });

            foreach (var photo in userToDelete.Photos)
                _context.Photos.Remove(photo);

            foreach (var userAtMeeting in userToDelete.UserAtMeetings)
            {
                foreach (var connectionLog in userAtMeeting.ConnectionLogs)
                    _context.ConnectionLogs.Remove(connectionLog);

                foreach (var camStatus in userAtMeeting.CamStatuses)
                    _context.CamStatuses.Remove(camStatus);

                _context.UsersAtMeeting.Remove(userAtMeeting);
            }

            try
            {
                await Task.Run(() => Directory.Delete(_staticPath + "/Photos/" + userToDelete.Id.ToString(), true));
                await Task.Run(() => Directory.Delete(_staticPath + "/Resources/Avatars/" + userToDelete.Id.ToString(), true));
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            await _userManager.DeleteAsync(userToDelete);

            return Ok();
        }

        [Authorize]
        [HttpPost(template: "ChangeAvatar")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<Object> ChangeAvatar([FromForm] IFormFile file)
        {
            var currentUser = await CurrentUser();

            if (currentUser == null)
            {
                return NotFound();
            }

            var userID = currentUser.Id;
            var userAvaPath = Path.Combine(Path.Combine(_staticPath, "Resources/Avatars/", userID.ToString() + "/"));

            if (currentUser.ApproveStatus == ApproveStatuses.Approved) 
            {
                var path = currentUser.AvatarPath.Replace(_host, "");

                using var stream = new MemoryStream(System.IO.File.ReadAllBytes(Path.Combine(userAvaPath, path.Split("/").Last())).ToArray());
                var file2 = new FormFile(stream, 0, stream.Length, "streamFile", "avatar.jpg");

                var res = (OkObjectResult)CompareFaces(file, file2).Result;

                if(res.Value.ToString() != "RECOGNIZED")
                    return StatusCode(StatusCodes.Status418ImATeapot,
                        new Response { Status = "Error", Message = "Try another photo" });
            }
            else
            {

                var res = (OkObjectResult)CheckFace(file).Result;

                if (res.Value.ToString() == "NOT_FACE")
                    return StatusCode(StatusCodes.Status418ImATeapot,
                        new Response { Status = "Error", Message = "Try another photo" });

                currentUser.ApproveStatus = ApproveStatuses.InProccess;
            }

            Random rnd = new Random();
            string avaName = "avatar" + rnd.Next(0, int.MaxValue).ToString() + ".jpg";

            string fPath = Path.Combine(userAvaPath, avaName);

            try
            {
                using (var stream = new FileStream(fPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new Response { Status = "Error", Message = "Error occured, while loading image to the file server" });
            }

            currentUser.AvatarPath = _host + "static/" + "Resources/Avatars/" + userID.ToString() + "/" + avaName;

            await _userManager.UpdateAsync(currentUser);

            return Ok();
        }

        /// <summary>
        /// Добавляет фото пользователя
        /// </summary>
        /// <param name="userID">ID пользователя</param>
        /// <param name="roomName">Название комнаты</param>
        /// <param name="file">Файл</param>
        /// <returns></returns>
        /// <response code="201">Фото добавлено</response>
        /// <response code="404">Пользователя не существует</response>
        /// <response code="500">Произошла ошибка, при сохранении фото в папку</response>
        //[AdminAuthorize]
        [AdminAuthorize]
        [HttpPost(template: "AddPhoto")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IFormFile>> AddPhoto([FromQuery] string userID, [FromQuery] string roomName, [FromForm] IFormFile file)
        {
            var user = await _context.Users.FindAsync(userID);

            var meeting = await _context.Meetings.Where(m => m.Name == roomName).FirstOrDefaultAsync();

            if (user == null || meeting == null)
            {
                return NotFound();
            }

            var userPath = Path.Combine(Path.Combine(_staticPath, "Photos/"), userID.ToString() + "/");
            var roomPath = Path.Combine(userPath, roomName);

            if (!Directory.Exists(roomPath)) 
            {
                try
                {
                    await Task.Run(() => Directory.CreateDirectory(roomPath));
                }
                catch (Exception)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError);
                }
            }

            string fPath = Path.Combine(roomPath + "/", file.FileName);

            try
            {
                using (var stream = new FileStream(fPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }
            catch(Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            user.Photos.Add(
                        new Photo
                        {
                            Path = _host + "static/" + "Photos/" + userID.ToString() + "/" + roomName + "/" + file.FileName,
                            Shot = DateTime.Now,
                            Meeting = meeting,
                            User = user
                        }
                    );

            await _userManager.UpdateAsync(user);

            return CreatedAtAction("AddPhoto", new { id =  user.Id}, user.Photos.Last());
        }

        /// <summary>
        /// Удаляет фото пользователя
        /// </summary>
        /// <param name="userID">ID пользователя</param>
        /// <param name="photoID">ID фото (из БД)</param>
        /// <returns></returns>
        /// <response code="200">Удаленное фото</response>
        /// <response code="404">Пользователя или фото не существует</response>
        /// <response code="500">Произошла ошибка, при удалении фото из папки</response>
        //[AdminAuthorize]
        [AdminAuthorize]
        [HttpDelete(template: "DeletePhoto")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<Photo>> DeletePhoto([FromQuery] string userID, [FromQuery] int photoID)
        {
            var user = await _context.Users.FindAsync(userID);

            if (user == null)
            {
                return NotFound();
            }

            var photo = await _context.Photos.Where(p => p.User == user && p.PhotoID == photoID).FirstOrDefaultAsync();

            if (photo == null)
            {
                return NotFound();
            }

            try
            {
                await Task.Run(() => System.IO.File.Delete(photo.Path));
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            _context.Photos.Remove(photo);
            await _context.SaveChangesAsync();

            return photo;
        }

        /// <summary>
        /// Получает фото пользователя
        /// </summary>
        /// <param name="userID">ID пользователя</param>
        /// <param name="photoID">ID фото (из БД)</param>
        /// <returns></returns>
        /// <response code="200">Фото</response>
        /// <response code="404">Пользователя или фото не существует</response>
        //[AdminAuthorize]
        [AdminAuthorize]
        [HttpGet(template: "GetPhoto")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Photo>> GetPhoto([FromQuery] string userID, [FromQuery] int photoID)
        {
            var user = await _context.Users.FindAsync(userID);

            if (user == null)
            {
                return NotFound();
            }

            var photo = await _context.Photos
                                        .Include(p => p.Meeting)
                                        .Where(p => p.User == user && p.PhotoID == photoID).FirstOrDefaultAsync();

            if (photo == null)
            {
                return NotFound();
            }

            return photo;
        }

        /// <summary>
        /// Получает все фото пользователя
        /// </summary>
        /// <param name="userID">ID пользователя</param>
        /// <returns></returns>
        /// <response code="200">Все фото</response>
        /// <response code="404">Пользователя не существует или нет фото</response>
        //[AdminAuthorize]
        [AdminAuthorize]
        [HttpGet(template: "GetPhotos")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<List<Photo>>> GetPhotos([FromQuery] string userID)
        {
            var user = await _context.Users.FindAsync(userID);

            if (user == null)
            {
                return NotFound();
            }

            var photos = await _context.Photos
                                    .Include(p => p.Meeting)
                                    .Where(p => p.User == user).ToListAsync();

            if (photos == null)
            {
                return NotFound();
            }

            return photos;
        }
    }
}
