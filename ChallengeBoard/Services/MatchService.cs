using System;
using System.Collections.Generic;
using System.Linq;
using ChallengeBoard.Email;
using ChallengeBoard.Email.Models;
using ChallengeBoard.Infrastucture;
using ChallengeBoard.Models;
using ChallengeBoard.Scoring;

namespace ChallengeBoard.Services
{
    public class MatchService : IMatchService
    {
        private readonly IRepository _repository;
        private readonly IMailService _mailService;

        public MatchService(IRepository repository, IMailService mailService)
        {
            _repository = repository;
            _mailService = mailService;
        }

        public void ConfirmMatch(int boardId, int matchId, string userName)
        {
            // Used to match against match Loser profile for verification of rejection authority.
            var userProfile = _repository.UserProfiles.FindProfile(userName);
            var board = _repository.GetBoardById(boardId);

            if (userProfile == null)
                throw new InvalidOperationException("Can not find your profile.");

            var confirmedMatch = _repository.GetMatchById(matchId);

            if (confirmedMatch == null)
                throw new ServiceException("Can not find match.");

            // Loser or Admin can approve
            if (confirmedMatch.Loser.ProfileUserId != userProfile.UserId && !board.IsOwner(userName))  
                throw new ServiceException("You are not able to approve this match.");

            if (confirmedMatch.ManuallyVerified.HasValue)
                throw new ServiceException("Your match verification has been upheld.");

            if (confirmedMatch.IsResolved)
                throw new ServiceException("This match has already been resolved.");

            confirmedMatch.ManuallyVerified = DateTime.Now;
            
            _repository.CommitChanges();
        }

        public Match CreateMatch(int boardId, string winnerName, string loserName, string winnerComment = "", bool tie = false)
        {
            var match = GenerateMatch(boardId, winnerName, loserName, tie);

            match.WinnerComment = winnerComment;

            _repository.Add(match);
            _repository.CommitChanges();

            _mailService.SendEmail(match.Loser.Profile.EmailAddress, match.Loser.Name,
                                   "Match Notification", EmailType.MatchNotification,
                                   new MatchNotification
                                   {
                                       WinnerName = match.Winner.Name,
                                       LoserName = match.Loser.Name,
                                       BoardName = match.Board.Name,
                                       WinnerComment = winnerComment,
                                       AutoVerifies = match.Board.AutoVerification
                                   });

            return (match);
        }

        public Match GenerateMatch(int boardId, string winnerName, string loserName, bool tie = false)
        {
            var board = _repository.GetBoardByIdWithCompetitors(boardId);

            if(board == null)
                throw (new ServiceException("Can not find challenge board."));

            if (DateTime.Now < board.Started)
                throw (new ServiceException("This challenge board start on " + board.Started.ToShortDateString() + "."));
            if (DateTime.Now >= board.End)
                throw (new ServiceException("This challenge board has ended."));

            var winner = board.Competitors.Active().FindCompetitorByName(winnerName);
            var loser = board.Competitors.Active().FindCompetitorByName(loserName);
            
            if(winner == null)
                throw (new ServiceException("You are not part of this challenge board."));
            if (loser == null)
                throw (new ServiceException("Can not find opponent."));
            if (loser.Name == winner.Name)
                throw (new ServiceException("You can't play yourself."));

            var match = new Match
            {
                Board = board,
                Tied = tie,
                Winner = winner,
                Loser = loser,
                Created = DateTime.Now,
                VerificationDeadline = DateTime.Now.AddHours(board.AutoVerification)
            };

            var unresolvedMatches = _repository.GetUnresolvedMatchesByBoardId(boardId, false).ToList();

            // TODO: create scoring system through a factory
            IScoringSystem system = new StandardElo();

            return system.Calculate(board.StartingRating, match, unresolvedMatches);
        }

        public CompetitorStats CalculateCompetitorStats(Competitor competitor, ICollection<Match> matches)
        {
            var statsByOpponent = new Dictionary<Competitor, PvpStats>();

            foreach (var match in matches.Select(match => match.ResultForCompetitor(competitor))
                                                                .Where(result => !result.Invalid)
                                                                .OrderBy(m => m.Opponent.Name))
            {
                PvpStats pvpStats;

                if (statsByOpponent.ContainsKey(match.Opponent))
                    pvpStats = statsByOpponent[match.Opponent];
                else
                {
                    pvpStats = new PvpStats { Opponent = match.Opponent };
                    statsByOpponent.Add(match.Opponent, pvpStats);
                }

                switch (match.Outcome)
                {
                    case MatchOutcome.Win:
                        pvpStats.Wins++;
                        break;
                    case MatchOutcome.Lose:
                        pvpStats.Loses++;
                        break;
                    case MatchOutcome.Tie:
                        pvpStats.Ties++;
                        break;
                    default:
                        continue;
                }

                pvpStats.EloNet += match.EloChange;

                statsByOpponent[match.Opponent] = pvpStats; // Update the dictionary
            }

            // Flatten dictionary and send it back
            var finalStats = new CompetitorStats();
            finalStats.Pvp.AddRange(statsByOpponent.Values.ToList().OrderByDescending(x=>x.EloNet));

            return (finalStats);
        }

        public void ProcessManualVerifications(int boardId)
        {
            // All unresolved matches for this challenge board.
            var unresolvedMatches = _repository.GetUnresolvedMatchesByBoardId(boardId);

            var competitorIds = new HashSet<int>();

            foreach (var match in unresolvedMatches.OrderBy(x => x.VerificationDeadline))
            {
                if (!match.ManuallyVerified.HasValue)
                {
                    // These guys have matches that haven't been manually verified.  From this point, we
                    // can not verify any matches in which they are part of
                    competitorIds.Add(match.Winner.CompetitorId);
                    competitorIds.Add(match.Loser.CompetitorId);
                }
                else if (!competitorIds.Contains(match.Winner.CompetitorId) &&
                         !competitorIds.Contains(match.Loser.CompetitorId))
                    VerifyMatch(match, false);
            }

            _repository.CommitChanges();
        }

        public void RejectMatch(int boardId, int matchId, string userName)
        {
            // Used to match against match Loser profile for verification of rejection authority.
            var competior = _repository.GetCompetitorByUserName(boardId, userName);
            var board = _repository.GetBoardById(boardId);

            bool adminRejection = false;

            if (competior == null)
                throw new InvalidOperationException("Can not find your profile.");
            
            // All unresolved matches for this challenge board.
            var unresolvedMatches =
                _repository.GetUnresolvedMatchesByBoardId(boardId).OrderBy(x => x.VerificationDeadline).ToList();

            var rejectedMatch = unresolvedMatches.SingleOrDefault(x => x.MatchId == matchId);

            if (rejectedMatch == null)
                throw new ServiceException("Can not find match.");
            
            if(board.IsOwner(competior)) // Board owner can reject anything
                adminRejection = true;
            else if (rejectedMatch.DoesNotInvolve(competior)) // Participants have a say
                throw new ServiceException("You are not able to modify this match.");

            if(rejectedMatch.IsResolved)
                throw new ServiceException("This match has already been resolved.");

            if (DateTime.Now > rejectedMatch.VerificationDeadline)
                throw new ServiceException("The deadline for rejecting this match has passed.");

            rejectedMatch.Invalidate(competior);
            
            // Anonymous list of unresolve matches taking place after the rejected match.
            // * These are the matches we need to recalculate
            var matchList =
                unresolvedMatches.Select(
                    x => new {x.MatchId, x.Created})
                                 .Where(x => x.Created >= rejectedMatch.Created && x.MatchId != rejectedMatch.MatchId);

            // TODO: create scoring system through a factory
            IScoringSystem system = new StandardElo();
            
            foreach (var match in matchList)
            {
                // Get unresolved matches prior to this one
                var filteredUnresolved =
                    unresolvedMatches.Where(x => x.Created <= match.Created && x.MatchId != match.MatchId).ToList();

                // Pick out the match to recalc and save
                var matchToRecalc = unresolvedMatches.First(x => x.MatchId == match.MatchId);

                // Run recalc
                system.Calculate(board.StartingRating, matchToRecalc, filteredUnresolved);
            }

            _repository.CommitChanges();

            if (rejectedMatch.Withdrawn)
            {
                _mailService.SendEmail(rejectedMatch.Loser.Profile.EmailAddress, rejectedMatch.Loser.Profile.UserName,
                    "Match Withdrawn", EmailType.MatchWithdrawlNotice,
                    new MatchWithdrawlNotice
                    {
                        Withdrawer = rejectedMatch.Winner.Name,
                        Withdrawee = rejectedMatch.Loser.Name,
                        BoardName = board.Name,
                    });
            }
            else
            {
                _mailService.SendEmail(rejectedMatch.Winner.Profile.EmailAddress, rejectedMatch.Winner.Profile.UserName,
                    "Match Rejected", EmailType.MatchRejectionNotice,
                    new MatchRejectionNotice
                    {
                        RejectorName = adminRejection ? "the board administrator" : rejectedMatch.Loser.Name,
                        RejectedName = rejectedMatch.Winner.Name,
                        BoardName = board.Name,
                        BoardOwnerName = board.Owner.Name
                    });
            }
        }

        public void SweepMatches()
        {
            //var matches = _repository.Matches.Where(m => !m.Verified && !m.Rejected);
            var matches = _repository.Matches.Where(m => m.VerificationDeadline < DateTime.Now && !m.Resolved.HasValue);

            foreach (var match in matches)
                VerifyMatch(match, false);

            _repository.CommitChanges();

            // Get all boards that have outstanding manual verifications
            var boardIds =
                _repository.Matches.Where(m => m.ManuallyVerified.HasValue && !m.Resolved.HasValue)
                           .Select(x => x.Board.BoardId)
                           .Distinct()
                           .ToList();
            
            foreach (var boardId in boardIds)
                ProcessManualVerifications(boardId);
        }

        public void VerifyMatch(int matchId)
        {
            var match = _repository.GetMatchById(matchId);

            if (match == null)
                throw (new ServiceException("Match not found."));

            VerifyMatch(match);
        }

        private void VerifyMatch(Match match, bool commit=true)
        {
            match.Winner.Rating += match.WinnerRatingDelta;
            match.Loser.Rating += match.LoserRatingDelta;
            
            if (match.Tied)
            {
                match.Winner.Ties += 1;
                match.Loser.Ties += 1;
                match.Winner.Streak = match.Loser.Streak = 0;
            }
            else
            {
                match.Loser.Loses += 1;
                match.Loser.Streak = 0;

                match.Winner.Wins += 1;
                match.Winner.Streak += 1;
            }
            
            match.Verified = true;
            match.Resolved = DateTime.Now;

            if (commit)
                _repository.CommitChanges();
        }
    }
}