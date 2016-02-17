//  Copyright 2016 North Carolina State University, Center for Geospatial Analytics & 
//  Forest Service Northern Research Station, Institute for Applied Ecosystem Studies
//  Authors:  Francesco Tonini, Brian R. Miranda

using Landis.Core;
using Landis.Library.AgeOnlyCohorts;
using Landis.SpatialModeling;
using System;

namespace Landis.Extension.BaseEDA
{
    public class Epidemic
        : ICohortDisturbance

    {
        private static IEcoregionDataset ecoregions;
        private IAgent epidemicParms;
        private double random;

        // - TOTAL -
        private int totalSitesInfected;
        private int totalSitesDiseased; //disease does not ALWAYS translated into mortality (damage)
        private int totalSitesDamaged;  //sites that are diseased AND experienced mortality
        private int totalCohortsKilled; //all mortality
        private int totalMortSppCohortsKilled; //only mortality of spp of interest (Mort Flag = YES)

        // - SITE -
        //private double siteVulnerability;
        private int siteCohortsKilled; //customize this to ONLY include the species of interest
        private int siteCFSconifersKilled;
        private int siteMortSppKilled; //spp that are included in the mortality species list
        private int[] sitesInEvent;

        private ActiveSite currentSite; // current site where cohorts are being affected

        // - Transmission - 
        private enum DispersalTemplate { PowerLaw, NegExp };   
        private enum InitialCondition   { map, none };   //is this to use initial map of infected sites?
        private enum SHImode { SHImax, SHImean };

        //---------------------------------------------------------------------
        static Epidemic()
        {
        }
        //---------------------------------------------------------------------
        public int TotalCohortsKilled
        {
            get {
                return totalCohortsKilled;
            }
        }
        //---------------------------------------------------------------------
        public int MortSppCohortsKilled
        {
            get
            {
                return totalMortSppCohortsKilled;
            }
        }
        //---------------------------------------------------------------------
        public int TotalSitesInfected
        {
            get
            {
                return totalSitesInfected;
            }
        }
        //---------------------------------------------------------------------
        public int TotalSitesDiseased
        {
            get
            {
                return totalSitesDiseased;
            }
        }
        //---------------------------------------------------------------------
        public int TotalSitesDamaged
        {
            get
            {
                return totalSitesDamaged;
            }
        }
        //---------------------------------------------------------------------
        ExtensionType IDisturbance.Type
        {
            get {
                return PlugIn.type;
            }
        }
        //---------------------------------------------------------------------
        ActiveSite IDisturbance.CurrentSite
        {
            get {
                return currentSite;
            }
        }
        //---------------------------------------------------------------------
        IAgent EpidemicParameters
        {
            get
            {
                return epidemicParms;
            }
        }
        //---------------------------------------------------------------------
        public int[] SitesInEvent
        {
            get
            {
                return sitesInEvent;
            }
        }
        //---------------------------------------------------------------------
        ///<summary>
        ///Initialize an Epidemic - defined as an agent outbreak for an entire landscape
        ///at a single EDA timestep.  One epidemic per agent per EDA timestep
        ///</summary>

        public static void Initialize(IAgent agent)
        {
            PlugIn.ModelCore.UI.WriteLine("   Initializing agent {0}.", agent.AgentName);

            ecoregions = PlugIn.ModelCore.Ecoregions;

            //.ActiveSiteValues allows you to reset all active site at once.
            SiteVars.SiteHostIndexMod.ActiveSiteValues = 0;
            SiteVars.SiteHostIndex.ActiveSiteValues = 0;

        }

        //---------------------------------------------------------------------
        ///<summary>
        ///Simulate an Epidemic - This is the controlling function that calls the
        ///subsequent function.  The basic logic of an epidemic resides here.
        ///</summary>
        public static Epidemic Simulate(IAgent agent, int currentTime, int agentIndex)
        {

            Epidemic CurrentEpidemic = new Epidemic(agent);
            PlugIn.ModelCore.UI.WriteLine("   New EDA Epidemic Started.");

            SiteResources.SiteHostIndexCompute(agent);   
            SiteResources.SiteHostIndexModCompute(agent);
            ClimateVariableDefinition.CalculateClimateVariables(agent);
            CurrentEpidemic.ComputeSiteInfStatus(agent, agentIndex);

            return CurrentEpidemic;
        }

        //---------------------------------------------------------------------
        // Epidemic Constructor
        private Epidemic(IAgent agent)
        {
            sitesInEvent = new int[ecoregions.Count];
            foreach(IEcoregion ecoregion in ecoregions)
                sitesInEvent[ecoregion.Index] = 0;
            epidemicParms = agent;
            totalCohortsKilled = 0;
            totalMortSppCohortsKilled = 0;
            totalSitesInfected = 0;
            totalSitesDiseased = 0;
            totalSitesDamaged = 0;
        }

        //---------------------------------------------------------------------
        //Go through all active sites and update their infection status according to the
        //probs of being S, I, D.
        private void ComputeSiteInfStatus(IAgent agent, int agentIndex)
        {

            double deltaPSusceptible = 0;  //do I need to initialize to 0 these?
            double deltaPInfected = 0;     //do I need to initialize to 0 these?
            double deltaPDiseased = 0;     //do I need to initialize to 0 these?

            int siteCohortsKilled = 0; //why initialize this here since you reset to 0 inside the foreach loop?
            int[] cohortsKilled = new int[3];

            //for each active site calculate the probability of changing status between S-I-D
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
            {

                siteCohortsKilled = 0; //see comment above...
                random = 0;

                double myRand = PlugIn.ModelCore.GenerateUniform();

                //get weather index for the current site
                double weatherIndex = SiteVars.ClimateVars[site]["AnnualWeatherIndex"];

                //calculate force of infection for current site                
                double FOI = ComputeSiteFOI(agent, site, weatherIndex, agentIndex); 

                deltaPSusceptible = -FOI * SiteVars.PSusceptible[site][agentIndex];
                deltaPInfected = FOI * SiteVars.PSusceptible[site][agentIndex] - agent.AcquisitionRate * SiteVars.PInfected[site][agentIndex];  //rD = acquisition rate
                deltaPDiseased = agent.AcquisitionRate * SiteVars.PInfected[site][agentIndex];

                //update probs of being in each considered status (S, I, D)
                SiteVars.PSusceptible[site][agentIndex] = + deltaPSusceptible;
                if (SiteVars.PSusceptible[site][agentIndex] > 1)
                    SiteVars.PSusceptible[site][agentIndex] = 1;
                //if (SiteVars.PSusceptible[site] < 0)
                //    SiteVars.PSusceptible[site] = 0;

                SiteVars.PInfected[site][agentIndex] = + deltaPInfected;
                if (SiteVars.PInfected[site][agentIndex] > 1)
                    SiteVars.PInfected[site][agentIndex] = 1;
                //if (SiteVars.PInfected[site] < 0)
                //    SiteVars.PInfected[site] = 0;

                SiteVars.PDiseased[site][agentIndex] = + deltaPDiseased;
                if (SiteVars.PDiseased[site][agentIndex] > 1)
                    SiteVars.PDiseased[site][agentIndex] = 1;
                //if (SiteVars.PDiseased[site] < 0)
                //    SiteVars.PDiseased[site] = 0;

                // SUSCEPTIBLE --->> INFECTED
                if (SiteVars.InfStatus[site][agentIndex] == 0 && SiteVars.PInfected[site][agentIndex] >= myRand)  //if site is Susceptible (S) 
                {
                    //update state of current site from S to I
                    SiteVars.InfStatus[site][agentIndex] = 1;
                    totalSitesInfected++;
                }

                // INFECTED --->> DISEASED -mortality-
                if (SiteVars.InfStatus[site][agentIndex] == 1 && SiteVars.PDiseased[site][agentIndex] >= myRand) //if site is "diseased" then apply the mortality to affected cohorts 
                {
                    totalSitesDiseased++;
                    random = myRand;
                    cohortsKilled = KillSiteCohorts(site);
                    siteCohortsKilled = cohortsKilled[0];
                    siteMortSppKilled = cohortsKilled[2];

                    
                    if (SiteVars.NumberCFSconifersKilled[site].ContainsKey(PlugIn.ModelCore.CurrentTime))
                    {
                        int prevKilled = SiteVars.NumberCFSconifersKilled[site][PlugIn.ModelCore.CurrentTime];
                        SiteVars.NumberCFSconifersKilled[site][PlugIn.ModelCore.CurrentTime] = prevKilled + cohortsKilled[1];
                    }
                    else
                    {
                        SiteVars.NumberCFSconifersKilled[site].Add(PlugIn.ModelCore.CurrentTime, cohortsKilled[1]);
                    }

                     
                    if (SiteVars.NumberMortSppKilled[site].ContainsKey(PlugIn.ModelCore.CurrentTime))
                    {
                        int prevKilled = SiteVars.NumberMortSppKilled[site][PlugIn.ModelCore.CurrentTime];
                        SiteVars.NumberMortSppKilled[site][PlugIn.ModelCore.CurrentTime] = prevKilled + cohortsKilled[2];
                    }
                    else
                    {
                        SiteVars.NumberMortSppKilled[site].Add(PlugIn.ModelCore.CurrentTime, cohortsKilled[2]);
                    }

                    //if there is at least one cohort killed by current epidemic event
                    if (siteCohortsKilled > 0)
                    {
                        totalCohortsKilled += siteCohortsKilled;  //cumulate number of cohorts killed
                        totalSitesDamaged++; //cumulate number of sites damaged
                        SiteVars.TimeOfLastEvent[site] = PlugIn.ModelCore.CurrentTime;

                        //if there is at least one cohort killed by current epidemic event (among selected species of interest - FLAG YES)
                        if (siteMortSppKilled > 0)
                            totalMortSppCohortsKilled += siteMortSppKilled; //cumulate number of cohorts killed
                    }
                }
            }
        }  

        //---------------------------------------------------------------------
        //A small helper function for going through list of cohorts at a site
        //and checking them with the filter provided by RemoveMarkedCohort(ICohort).
        private int[] KillSiteCohorts(ActiveSite site)
        {
            siteCohortsKilled = 0;
            siteCFSconifersKilled = 0;
            siteMortSppKilled = 0;

            currentSite = site;

            SiteVars.Cohorts[site].RemoveMarkedCohorts(this);

            int[] cohortsKilled = new int[3];

            cohortsKilled[0] = siteCohortsKilled;
            cohortsKilled[1] = siteCFSconifersKilled;
            cohortsKilled[2] = siteMortSppKilled;

            return cohortsKilled;
        }        
        
        //---------------------------------------------------------------------
        // This is a filter to determine which cohorts are removed.
        // Each cohort is passed into the function and tested whether it should
        // be killed.
        bool ICohortDisturbance.MarkCohortForDeath(ICohort cohort)
        {
            
            bool killCohort = false;

            ISppParameters sppParms = epidemicParms.SppParameters[cohort.Species.Index];
 
            if (cohort.Age >= sppParms.LowVulnHostAge)
            {
                if (random <= sppParms.LowVulnHostMortProb)
                   killCohort = true;
            }

            if (cohort.Age >= sppParms.MediumVulnHostAge)
            {
                if (random <= sppParms.MediumVulnHostMortProb)
                    killCohort = true;
            }

            if (cohort.Age >= sppParms.HighVulnHostAge)
            {
                if (random <= sppParms.HighVulnHostMortProb)
                    killCohort = true;
            }

            if (killCohort)
            {
                siteCohortsKilled++;

                if (sppParms.CFSConifer)
                    siteCFSconifersKilled++;

                if (sppParms.MortSppFlag)
                    siteMortSppKilled++;

            }

            return killCohort;
        }

        // check if the coordinates are inside the map 
        private bool isInside(int x, int y) 
        {
            return (x >= 1 && y >= 1 && x <= PlugIn.ModelCore.Landscape.Dimensions.Columns && y <= PlugIn.ModelCore.Landscape.Dimensions.Rows); 
        }

        ////-------------------------------------------------------
        /////<summary>
        /////Calculate the distance between two Sites
        /////</summary>
        //public static double DistanceBetweenSites(Site a, Site b)
        //{

        //    int Col = (int)a.Location.Column - (int)b.Location.Column;
        //    int Row = (int)a.Location.Row - (int)b.Location.Row;

        //    double aSq = System.Math.Pow(Col, 2);
        //    double bSq = System.Math.Pow(Row, 2);
        //    //PlugIn.ModelCore.Log.WriteLine("Col={0}, Row={1}.", Col, Row);
        //    //PlugIn.ModelCore.Log.WriteLine("aSq={0}, bSq={1}.", aSq, bSq);
        //    //PlugIn.ModelCore.Log.WriteLine("Distance in Grid Units = {0}.", System.Math.Sqrt(aSq + bSq));
        //    return (System.Math.Sqrt(aSq + bSq) * (double)PlugIn.ModelCore.CellLength);

        //}

        //-------------------------------------------------------
        //Calculate the distance from a location to a center
        //point (row and column = 0).
        private static double DistanceFromCenter(Site site, double row, double column)
        {
            double CellLength = PlugIn.ModelCore.CellLength;
            //row = System.Math.Abs(row) * CellLength;
            //column = System.Math.Abs(column) * CellLength;
            double dx = (row - site.Location.Row) * CellLength;
            double dy = (column - site.Location.Column) * CellLength;
            //double aSq = System.Math.Pow(column, 2);
            //double bSq = System.Math.Pow(row, 2);
            //return System.Math.Sqrt(aSq + bSq);
            return System.Math.Sqrt(System.Math.Pow(dx,2) + System.Math.Pow(dy,2));
        }

        //define func to calculate Force of Infection (FOI) for a given agent and site
        //force of infection depends on the dispersal kernel, weather index, SHI of neighboring sites and itself, pInfected & pDiseased of neighboring sites         
        double ComputeSiteFOI(IAgent agent, Site targetSite, double beta, int agentIndex)
        {
            double kernelProb, cumSum = 0.0, forceOfInf = 0.0, centroidDistance = 0.0, CellLength = PlugIn.ModelCore.CellLength;
            int source_x, source_y;            
            int maxRadius = agent.DispersalMaxDist;
            int numCellRadius = (int) (maxRadius / CellLength);
            Dispersal dsp = new Dispersal();

            PlugIn.ModelCore.UI.WriteLine("Looking for infection sources within the chosen neighborhood...");

            for (int row = (numCellRadius * -1); row <= numCellRadius; row++)
            {
                for (int col = (numCellRadius * -1); col <= numCellRadius; col++)
                {

                    if (row == 0 && col == 0) continue; //we do not want to consider source cells overlapping with target (current) cell

                    //calculate location of source pixel 
                    source_x = targetSite.Location.Row + row;
                    source_y = targetSite.Location.Column + col;

                    if (isInside(source_x, source_y))
                    {
                        Site sourceSite = PlugIn.ModelCore.Landscape[source_x, source_y];
                        if (sourceSite != null && sourceSite.IsActive)
                        {
                            //distance of source pixel from current target site
                            centroidDistance = DistanceFromCenter(targetSite, source_x, source_y);
                            //check if source cell is within max disp dist
                            if (centroidDistance <= maxRadius && centroidDistance > 0)
                            {
                                //check if source pixel is infectious (=infected or diseased):
                                if (SiteVars.InfStatus[sourceSite][agentIndex] == 1 || SiteVars.InfStatus[sourceSite][agentIndex] == 2)
                                {
                                    //read kernel prob
                                    kernelProb = dsp.GetDispersalProbability(centroidDistance);
                                    //A_j: site host index modified -source-
                                    //B_i: site host index modified -target, current site-
                                    
                                    //sum(A_j * B_i * P_Ij * P_Dj * Kernel(d_ij))
                                    cumSum =+ SiteVars.SiteHostIndexMod[sourceSite] * SiteVars.SiteHostIndexMod[targetSite] *
                                                      SiteVars.PInfected[sourceSite][agentIndex] * SiteVars.PDiseased[sourceSite][agentIndex] * kernelProb;
                                }//end check if source site is infectious
                            }//end check if distance < maxdist

                        }//end check if source site is NOT null AND Active
                    }//end check if source isInside

                }//end col loop                    
            }//end row loop

            //calculate force of infection: beta * cumSum
            forceOfInf = beta * cumSum;

            return forceOfInf;
        }

        //define function to calculate weather index for a given agent and site
        double CalculateWeatherIndex(IAgent agent, Site site)
        {
            double weatherIndex = 1;

            foreach(string weatherVar in agent.WeatherIndexVars)
            {
                foreach(DerivedClimateVariable derClimVar in agent.DerivedClimateVars)
                {
                    if (derClimVar.Name == weatherVar)
                    {

                    }
                }
                if(weatherVar == "TempIndex")
                {
                    int indexa = agent.VarFormula.Parameters.FindIndex(i => i == "a");
                    double a = Double.Parse(agent.VarFormula.Values[indexa]);
                    int indexb = agent.VarFormula.Parameters.FindIndex(i => i == "b");
                    double b = Double.Parse(agent.VarFormula.Values[indexb]);
                    int indexc = agent.VarFormula.Parameters.FindIndex(i => i == "c");
                    double c = Double.Parse(agent.VarFormula.Values[indexc]);
                    int indexd = agent.VarFormula.Parameters.FindIndex(i => i == "d");
                    double d = Double.Parse(agent.VarFormula.Values[indexd]);
                    int indexe = agent.VarFormula.Parameters.FindIndex(i => i == "e");
                    double e = Double.Parse(agent.VarFormula.Values[indexe]);
                    int indexf = agent.VarFormula.Parameters.FindIndex(i => i == "f");
                    double f = Double.Parse(agent.VarFormula.Values[indexf]);
                    int indexVar = agent.VarFormula.Parameters.FindIndex(i => i == "Variable");
                    string variableName = agent.VarFormula.Values[indexVar];

                    double variable = 0;
                    foreach(IClimateVariableDefinition climateVar in agent.ClimateVars)
                    {
                        if(climateVar.Name == variableName)
                        {
                            variable = SiteVars.ClimateVars[site][variableName];
                        }
                    }
                    //tempIndex = a + b * exp(c[ln(Variable / d) / e] ^ f);
                    double tempIndex = a + b * Math.Exp(c * Math.Pow((Math.Log(variable / d) / e),f));
                    

                    weatherIndex *= tempIndex;
                }
            }


            return weatherIndex;
        }

    }



}

