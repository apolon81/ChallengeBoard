using ChallengeBoard.Models;
using System.Collections.Generic;

namespace ChallengeBoard.Scoring
{
    public interface IScoringSystem
    {
        Match Calculate(double boardStartingRating, Match match, IList<Match> unresolvedMatches);
    }
}
