//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace TeamBins.DataAccess
{
    using System;
    using System.Collections.Generic;
    
    public partial class Team
    {
        public Team()
        {
            this.Activities = new HashSet<Activity>();
            this.Issues = new HashSet<Issue>();
            this.TeamMembers = new HashSet<TeamMember>();
            this.TeamMemberRequests = new HashSet<TeamMemberRequest>();
        }
    
        public int ID { get; set; }
        public string Name { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public int CreatedByID { get; set; }
    
        public virtual ICollection<Activity> Activities { get; set; }
        public virtual ICollection<Issue> Issues { get; set; }
        public virtual User CreatedBy { get; set; }
        public virtual ICollection<TeamMember> TeamMembers { get; set; }
        public virtual ICollection<TeamMemberRequest> TeamMemberRequests { get; set; }
    }
}
