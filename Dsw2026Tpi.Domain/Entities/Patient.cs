using System;
using System.Collections.Generic;
using System.Text;

namespace Dsw2026Tpi.Domain.Entities
{
    public class Patient : EntityBase
    {
        public string UserId { get; set; }
        public string Dni {  get; set; }
        public string? FullName { get; private set; }

        #region Constructor for EF
#pragma warning disable CS8618
        private Patient() { }
#pragma warning restore CS8618
        #endregion

        public Patient(string userId, string dni, string? fullName = null, Guid? id = null) : base(id)
        {
            UserId = userId;
            Dni = dni;
            FullName = fullName;
        }
    }



}
