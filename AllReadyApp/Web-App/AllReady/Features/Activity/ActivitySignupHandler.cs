﻿using AllReady.Features.Notifications;
using AllReady.Models;
using AllReady.Models.Notifications;
using AllReady.Services;
using MediatR;
using Microsoft.Data.Entity;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AllReady.Features.Activity
{
    public class ActivitySignupHandler : RequestHandler<ActivitySignupCommand>
    {
        private readonly IMediator _bus;
        private readonly AllReadyContext _context;

        public ActivitySignupHandler(IMediator bus, AllReadyContext context)
        {
            _bus = bus;
            _context = context;
        }

        protected override void HandleCore(ActivitySignupCommand message)
        {
            var activitySignup = message.ActivitySignup;
            var user = _context.Users
                .Include(u => u.AssociatedSkills)
                .SingleOrDefault(u => u.Id == activitySignup.UserId);
            var activity = _context.Activities
                .Include(a => a.RequiredSkills)
                .Include(a => a.UsersSignedUp).ThenInclude(u => u.User)
                .SingleOrDefault(a => a.Id == activitySignup.ActivityId);

            activity.UsersSignedUp = activity.UsersSignedUp ?? new List<ActivitySignup>();

            // If the user is already signed up for some reason, stop don't signup again, please
            if (!activity.UsersSignedUp.Any(acsu => acsu.User.Id == user.Id))
            {
                activity.UsersSignedUp.Add(new ActivitySignup
                {
                    Activity = activity,
                    User = user,
                    PreferredEmail = activitySignup.PreferredEmail,
                    PreferredPhoneNumber = activitySignup.PreferredPhoneNumber,
                    AdditionalInfo = activitySignup.AdditionalInfo,
                    SignupDateTime = DateTime.UtcNow
                });

                _context.Update(activity);

                //Add selected new skills (if any) to the current user
                if (activitySignup.AddSkillIds.Count > 0)
                {
                    var skillsToAdd = activity.RequiredSkills
                        .Where(acsk => activitySignup.AddSkillIds.Contains(acsk.SkillId))
                        .Select(acsk => new UserSkill() { SkillId = acsk.SkillId, UserId = user.Id });
                    user.AssociatedSkills.AddRange(skillsToAdd.Where(toAdd => !user.AssociatedSkills.Any(existing => existing.SkillId == toAdd.SkillId)));

                    _context.Update(user);
                }

                _context.SaveChanges();

                //Notify admins of a new volunteer
                _bus.Publish(new VolunteerInformationAdded()
                {
                    ActivityId = activitySignup.ActivityId,
                    UserId = activitySignup.UserId
                });
            }
        }
    }
}
