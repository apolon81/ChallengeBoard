using System;
using ChallengeBoard.Models;
using ChallengeBoard.Infrastucture;
using System.Collections.Generic;

namespace ChallengeBoard.Scoring
{
    [ScoringSystem("Glicko","Glicko scoring system")]
    public class Glicko : IScoringSystem
    {
        // Rating period in days. This is used only to determine the number of rating periods
        // since last competition in rating deviation formula. Ratings and deviations are
        // reacalculated on a game by game basis rather then in periods.
        private int ratingPeriod = 1;

        // This constant influences current rating deviance calculations.
        private double c = 60.0;

        private double maximumDeviance = 350;

        // TODO: new players: r = 1500, RD = 350

        /// <summary>
        /// Class for calculating player Glicko gain/loss
        /// </summary>
        public Glicko() { }

        private void Calculate(Dictionary<string, double> playerData, Dictionary<string, double> opponentData, double result)
        {
            double q = 0.0057565;

            double g = 1.0 / Math.Sqrt(1 + 3 * q * q * opponentData["rating deviance"] * opponentData["rating deviance"] / (Math.PI * Math.PI));

            double E = 1.0 / (1 + Math.Pow(10.0, -1 * g * (playerData["rating"] - opponentData["rating"]) / 400));

            double d = Math.Pow(q * q * g * g * E * (1 - E), -1.0);

            playerData["rating delta"] = q * g * (result - E) / (1.0 / Math.Pow(playerData["rating deviance"], 2.0) + 1.0 / d); 

            double newDeviance = Math.Sqrt(Math.Pow(1.0 / Math.Pow(playerData["rating deviance"], 2.0) + 1.0 / d, -1.0));

            playerData["rating deviance delta"] = newDeviance - playerData["rating deviance"];
        }

        Match IScoringSystem.Calculate(double boardStartingRating, Models.Match match, System.Collections.Generic.IList<Models.Match> unresolvedMatches, Match latestWinnersMatch, Match latestLosersMatch)
        {

            // Determine unverified information about the winner
            var winnerData = new Dictionary<string, double>();
            GatherPlayerData(winnerData, match.Winner, unresolvedMatches, latestWinnersMatch);
            
            // Determine unverified information about the loser
            var loserData = new Dictionary<string, double>();
            GatherPlayerData(loserData, match.Loser, unresolvedMatches, latestLosersMatch);

            // Calculate the current rating deviance (pre match)
            winnerData["rating deviance"] = CalculateCurrentRatingDeviance(winnerData["rating deviance"], (int)winnerData["rating periods passed"]);
            loserData["rating deviance"] = CalculateCurrentRatingDeviance(loserData["rating deviance"], (int)loserData["rating periods passed"]);

            Calculate(winnerData, loserData, match.Tied ? 0.5 : 1);
            Calculate(loserData, winnerData, match.Tied ? 0.5 : 0);

            // Update the ratings
            match.WinnerRatingDelta = winnerData["rating delta"].RoundToWhole();
            match.LoserRatingDelta = loserData["rating delta"].RoundToWhole();

            match.WinnerEstimatedRating = (int)winnerData["rating"] + match.WinnerRatingDelta;
            match.LoserEstimatedRating = (int)loserData["rating"] + match.LoserRatingDelta;

            // Update the rating deviances
            match.WinnerDevianceDelta = winnerData["rating deviance delta"].RoundToWhole();
            match.LoserDevianceDelta = loserData["rating deviance delta"].RoundToWhole();

            match.WinnerEstimatedDeviance = (int)winnerData["rating deviance"] + match.WinnerDevianceDelta;
            match.LoserEstimatedDeviance = (int)loserData["rating deviance"] + match.LoserDevianceDelta;

            return match;
        }

        private void GatherPlayerData(Dictionary<string, double> data, Competitor competitor, System.Collections.Generic.IList<Models.Match> unresolvedMatches, Match latestMatch)
        {
            data.Add("rating", competitor.CalculateUnverifiedRank(unresolvedMatches));
            data.Add("rating deviance", competitor.CalculateUnverifiedDeviance(unresolvedMatches));
            data.Add("rating periods passed", competitor.CalculateUnverifiedInactivity(latestMatch, ratingPeriod));
        }

        private double CalculateCurrentRatingDeviance(double lastKnownDeviance, int ratingPeriodsPassed)
        {
            return Math.Min(Math.Sqrt(lastKnownDeviance * lastKnownDeviance + c * c * ratingPeriodsPassed), maximumDeviance);
        }
    }
}