using System;
using ChallengeBoard.Models;
using ChallengeBoard.Infrastucture;

namespace ChallengeBoard.Scoring
{
    [ScoringSystem("Glicko","Glicko scoring system")]
    public class Glicko : IScoringSystem
    {
        // Rating Disparity. The higher is F, the easier it is to gain points (or to lose them)
        private const int F = 400;

        /// <summary>
        /// Class for calculating player ELO gain/loss
        /// </summary>
        public Glicko() { }

        private ScoringResult Calculate(double boardStartingRating, double winnerRating, double loserRating, bool tie = false)
        {
            //http://en.wikipedia.org/wiki/Elo_rating_system
            //http://www.chess-mind.com/en/elo-system

            var winnerK = CalculateKFactor(boardStartingRating, winnerRating);
            var loserK = CalculateKFactor(boardStartingRating, loserRating);

            var eW = 1 / (1 + Math.Pow(10,(loserRating - winnerRating)/F));
            var eL = 1 / (1 + Math.Pow(10,(winnerRating - loserRating)/F));

            var results = new ScoringResult();

            if (!tie)
            {
                results.WinnerDelta = Math.Round(winnerK * (1 - eW));
                results.LoserDelta = Math.Round(loserK * (0 - eL));
            }
            else
            {
                results.WinnerDelta = Math.Round(winnerK * (.5 - eW));
                results.LoserDelta = Math.Round(loserK * (.5 - eL));
            }

            return (results);
        }

        private static int CalculateKFactor(double boardStartingRating, double competitorRating)
        {
            // Game Importance.
            if (competitorRating >= (boardStartingRating * 1.5)) // 2400
                return (KFactor.Low);
            
            if (competitorRating >= (boardStartingRating * 1.3125)) // 2100
                return (KFactor.Medium);

            return (KFactor.High);
        }

        Match IScoringSystem.Calculate(double boardStartingRating, Models.Match match, System.Collections.Generic.IList<Models.Match> unresolvedMatches)
        {
            // Figure unverified ratings.  Parses and sums unverified matches
            var unverifiedWinnerRank = match.Winner.CalculateUnverifiedRank(unresolvedMatches);
            var unverifiedLoserRank = match.Loser.CalculateUnverifiedRank(unresolvedMatches);

            var eloResult = Calculate(boardStartingRating, unverifiedWinnerRank, unverifiedLoserRank, match.Tied);

            // Update the ratings
            match.WinnerRatingDelta = eloResult.WinnerDelta.RoundToWhole();
            match.LoserRatingDelta = eloResult.LoserDelta.RoundToWhole();

            match.WinnerEstimatedRating = unverifiedWinnerRank + match.WinnerRatingDelta;
            match.LoserEstimatedRating = unverifiedLoserRank + match.LoserRatingDelta;

            return match;
        }
    }
}