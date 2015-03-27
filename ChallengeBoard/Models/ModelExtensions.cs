﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace ChallengeBoard.Models
{
    public static class ModelExtensions
    {
        // Board
        public static bool IsOwner(this Board board, string name)
        {
            return (board.Owner.Profile.UserName.Equals(name, StringComparison.InvariantCultureIgnoreCase));
        }

        public static bool IsOwner(this Board board, Competitor competitor)
        {
            return (board.Owner.CompetitorId == competitor.CompetitorId);
        }

        // Competitor
        public static bool CanEdit(this Competitor competitor, Board board, string name)
        {
            return (board.IsOwner(name) || competitor.Is(name));
        }

        public static bool Is(this Competitor competitor, string name)
        {
            return (competitor.Profile.UserName.Equals(name, StringComparison.InvariantCultureIgnoreCase));
        }

        public static IEnumerable<Competitor> Active(this ICollection<Competitor> competitors)
        {
            return (competitors.Where(x => x.Status == CompetitorStatus.Active));
        }

        public static IEnumerable<Competitor> Retired(this ICollection<Competitor> competitors)
        {
            return (competitors.Where(x => x.Status == CompetitorStatus.Retired));
        }

        public static bool ContainsCompetitor(this IEnumerable<Competitor> competitors, string name)
        {
            return (competitors.Any(x => x.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)));
        }

        public static Competitor FindCompetitorByName(this IEnumerable<Competitor> competitors, string name)
        {
            return (competitors.SingleOrDefault(x => x.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)));
        }

        public static Competitor FindCompetitorById(this IEnumerable<Competitor> competitors, int id)
        {
            return (competitors.SingleOrDefault(x => x.CompetitorId.Equals(id)));
        }

        public static Competitor FindCompetitorByProfileId(this IEnumerable<Competitor> competitors, int id)
        {
            return (competitors.SingleOrDefault(x => x.ProfileUserId.Equals(id)));
        }

        public static Competitor FindCompetitorByUserName(this IEnumerable<Competitor> competitors, string userName)
        {
            return
                (competitors.SingleOrDefault(
                    x => x.Profile.UserName.Equals(userName, StringComparison.InvariantCultureIgnoreCase)));
        }

        public static int CalculateUnverifiedRank(this Competitor competitor , IList<Match> matches)
        {
            return (competitor.Rating +
                    matches.Where(x => x.Loser.ProfileUserId == competitor.ProfileUserId && !x.IsResolved)
                           .Sum(x => x.LoserRatingDelta) +
                    matches.Where(x => x.Winner.ProfileUserId == competitor.ProfileUserId && !x.IsResolved)
                           .Sum(x => x.WinnerRatingDelta));
        }

        public static int CalculateUnverifiedDeviance(this Competitor competitor, IList<Match> matches)
        {
            return (competitor.Deviance +
                    matches.Where(x => x.Loser.ProfileUserId == competitor.ProfileUserId && !x.IsResolved)
                           .Sum(x => x.LoserDevianceDelta) +
                    matches.Where(x => x.Winner.ProfileUserId == competitor.ProfileUserId && !x.IsResolved)
                           .Sum(x => x.WinnerDevianceDelta));
        }

        public static int CalculateUnverifiedInactivity(this Competitor competitor, Match latestMatch, int ratingPeriod)
        {
            // TODO: return 0 here if the user hasn't played any games yet
            return 1;
        }

        // Match
        public static MatchResults ResultForCompetitor(this Match match, Competitor competitor)
        {
            var result = new MatchResults();
            
            if (match.Winner.CompetitorId != competitor.CompetitorId && match.Loser.CompetitorId != competitor.CompetitorId)
                result.Outcome = MatchOutcome.NotInvolved;
            else if (competitor.CompetitorId == match.Winner.CompetitorId)
            {
                result.Outcome = MatchOutcome.Win;
                result.EloChange = match.WinnerRatingDelta;
                result.Opponent = match.Loser;
            }
            else
            {
                result.Outcome = MatchOutcome.Lose;
                result.EloChange = match.LoserRatingDelta;
                result.Opponent = match.Winner;
            }

            if (match.Tied)
                result.Outcome = MatchOutcome.Tie;

            if (match.IsInvalid)
                result.Invalid = true;

            return (result);
        }

        public static bool Involves(this Match match, Competitor competitor)
        {
            return match.Winner.CompetitorId == competitor.CompetitorId ||
                   match.Loser.CompetitorId == competitor.CompetitorId;
        }

        public static bool DoesNotInvolve(this Match match, Competitor competitor)
        {
            return !Involves(match, competitor);
        }

        // Post
        public static bool IsOwner(this Post post, string name)
        {
            return (post.Owner.Profile.UserName.Equals(name, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    public class MatchResults
    {
        public MatchOutcome Outcome { get; set; }
        public Competitor Opponent { get; set; }
        public int EloChange { get; set; }
        public bool Invalid { get; set; }
    }

    public enum MatchOutcome
    {
        Win = 0,
        Lose,
        Tie,
        NotInvolved
    }
}