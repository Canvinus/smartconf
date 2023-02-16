using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace ezmeetsAPI.Models
{
    public class Meeting
    {
        public int MeetingID { get; set; }
        public bool isLive { get { if (DateTime.Now >= StartTime && !hasEnded) return true; else return false; } }
        [Required]
        public string Name { get; set; }
        public DateTime StartTime { get; set; }
        public int Duration { get; set; }
        public DateTime EndingTime { get; set; }
        public bool hasEnded { get { if (DateTime.Now >= EndingTime) return true; else return false; } }
        public string Description { get; set; }
        public bool botAutoStart { get; set; } = true;
        public bool botIsLive { get; set; } = false;
        public List<AllowedUser> AllowedUsers { get; set; } = new List<AllowedUser>();
        public List<UserAtMeeting> UsersAtMeeting { get; set; } = new List<UserAtMeeting>();
        [JsonIgnore]
        public List<Photo> Photos { get; set; } = new List<Photo>();

    }
}
