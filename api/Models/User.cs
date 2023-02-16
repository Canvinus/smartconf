using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace ezmeetsAPI.Models
{
    public class User : IdentityUser
    {
        public string AvatarPath { get; set; }

        public string FullName { get; set; }

        public string Role { get; set; }

        public string ApproveStatus { get; set; } = ApproveStatuses.NotApproved;

        [NotMapped]
        public List<GroupShort> Groups { get; set; } = new List<GroupShort>();

        [JsonIgnore]
        public List<Photo> Photos { get; set; } = new List<Photo>();

        [JsonIgnore]
        public List<UserAtMeeting> UserAtMeetings { get; set; } = new List<UserAtMeeting>();

        [JsonIgnore]
        public override string NormalizedUserName
        {
            get { return base.NormalizedUserName; }
            set { base.NormalizedUserName = value; }
        }

        [JsonIgnore]
        public override string NormalizedEmail
        {
            get { return base.NormalizedEmail; }
            set { base.NormalizedEmail = value; }
        }

        [JsonIgnore]
        public override bool EmailConfirmed
        {
            get { return base.EmailConfirmed; }
            set { base.EmailConfirmed = value; }
        }

        [JsonIgnore]
        public override string PasswordHash
        {
            get { return base.PasswordHash; }
            set { base.PasswordHash = value; }
        }

        [JsonIgnore]
        public override string SecurityStamp
        {
            get { return base.SecurityStamp; }
            set { base.SecurityStamp = value; }
        }

        [JsonIgnore]
        public override string ConcurrencyStamp
        {
            get { return base.ConcurrencyStamp; }
            set { base.ConcurrencyStamp = value; }
        }

        [JsonIgnore]
        public override string PhoneNumber
        {
            get { return base.PhoneNumber; }
            set { base.PhoneNumber = value; }
        }

        [JsonIgnore]
        public override bool PhoneNumberConfirmed
        {
            get { return base.PhoneNumberConfirmed; }
            set { base.PhoneNumberConfirmed = value; }
        }

        [JsonIgnore]
        public override bool TwoFactorEnabled
        {
            get { return base.TwoFactorEnabled; }
            set { base.TwoFactorEnabled = value; }
        }

        [JsonIgnore]
        public override bool LockoutEnabled
        {
            get { return base.LockoutEnabled; }
            set { base.LockoutEnabled = value; }
        }

        [JsonIgnore]
        public override DateTimeOffset? LockoutEnd
        {
            get { return base.LockoutEnd; }
            set { base.LockoutEnd = value; }
        }

        [JsonIgnore]
        public override int AccessFailedCount
        {
            get { return base.AccessFailedCount; }
            set { base.AccessFailedCount = value; }
        }
    }
}
