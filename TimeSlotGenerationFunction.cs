using System;
using System.Configuration;
using System.Data.SqlClient;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BookMyTableFunctionApp
{
    public class TimeSlotGenerationFunction
    {
        private readonly IConfiguration _configuration;
        public TimeSlotGenerationFunction(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [Function("TimeSlotGenerationFunction")]
        public async Task Run([TimerTrigger("*/2 * * * *")] TimerInfo myTimer, FunctionContext context)
        {
            var log = context.GetLogger("TimeSlotGenerationFunction");
                log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            try
            {
                string connectionString = _configuration.GetConnectionString("DbContext");

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    string getLastReservationDateQuery = @"
            SELECT DiningTables.RestaurantBranchId, MAX(TimeSlots.ReservationDay) AS LastReservationDate
            FROM DiningTables
            LEFT OUTER JOIN TimeSlots ON DiningTables.Id = TimeSlots.DiningTableId
            GROUP BY DiningTables.RestaurantBranchId";

                    SqlCommand getLastReservationDateCommand = new SqlCommand(getLastReservationDateQuery, conn);

                    List<(int BranchId, DateTime LastReservationDate)> branchData = new List<(int, DateTime)>();

                    using (SqlDataReader reader = await getLastReservationDateCommand.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int branchId = (int)reader["RestaurantBranchId"];
                            DateTime lastReservationDate = (DateTime)reader["LastReservationDate"];
                            branchData.Add((branchId, lastReservationDate));
                        }
                    }

                    // Process each branch
                    foreach (var data in branchData)
                    {
                        int branchId = data.BranchId;
                        DateTime lastReservationDate = data.LastReservationDate;

                        // Calculate the reservation end date (current date + 1 or 2 days)
                        DateTime currentDate = DateTime.Now.Date;
                        DateTime reservationEndDate = currentDate > lastReservationDate ? currentDate.AddDays(2) : lastReservationDate.AddDays(2);

                        if (lastReservationDate <= currentDate.AddDays(2))
                        {


                            // Query to get the DiningTableIds for the branch
                            string getDiningTableIdsQuery = @"
                            SELECT Id AS DiningTableId
                            FROM DiningTables
                            WHERE RestaurantBranchId = @BranchId";

                            SqlCommand getDiningTableIdsCommand = new SqlCommand(getDiningTableIdsQuery, conn);
                            getDiningTableIdsCommand.Parameters.AddWithValue("@BranchId", branchId);

                            List<int> diningTableIds = new List<int>();

                            using (SqlDataReader diningTableIdReader = await getDiningTableIdsCommand.ExecuteReaderAsync())
                            {
                                while (await diningTableIdReader.ReadAsync())
                                {
                                    int diningTableId = (int)diningTableIdReader["DiningTableId"];
                                    diningTableIds.Add(diningTableId);
                                }
                            }

                            // Generate and insert new timeslots for the next 1 or 2 days for each dining table
                            foreach (int diningTableId in diningTableIds)
                            {
                                for (DateTime reservationDate = lastReservationDate.AddDays(1); reservationDate <= reservationEndDate; reservationDate = reservationDate.AddDays(1))
                                {
                                    // Insert available slots into the Timeslots table for each meal type
                                    foreach (string mealType in new string[] { "Breakfast", "Lunch", "Dinner" })
                                    {
                                        string insertTimeslotQuery = @"
                            INSERT INTO TimeSlots (DiningTableId, ReservationDay, MealType, TableStatus)
                            VALUES (@DiningTableId, @ReservationDay, @MealType, @TableStatus)";

                                        SqlCommand insertTimeslotCommand = new SqlCommand(insertTimeslotQuery, conn);
                                        insertTimeslotCommand.Parameters.AddWithValue("@DiningTableId", diningTableId);
                                        insertTimeslotCommand.Parameters.AddWithValue("@ReservationDay", reservationDate);
                                        insertTimeslotCommand.Parameters.AddWithValue("@MealType", mealType);
                                        insertTimeslotCommand.Parameters.AddWithValue("@TableStatus", "Available");

                                        await insertTimeslotCommand.ExecuteNonQueryAsync();
                                    }
                                }
                            }
                        }
                    }
                }

                log.LogInformation($"Timeslot generation completed at: {DateTime.Now}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "An error occurred while processing the request.");
            }
        }
    }
}


// The TimerTrigger attribute in Azure Functions is used to schedule the execution of a function at specific time intervals based on a cron expression. The cron expression defines when the function should run by specifying minute, hour, day of the month, month, and day of the week.

//In the case of TimerTrigger("0 */
//12 * **"):

//0: Specifies the 0th minute of the specified hour, meaning the function will run at the start of the specified hour.

//*/12: Specifies that the function should run every 12 hours.

//*: Represents all possible values, meaning the function can run on any day of the month and any day of the week.

//So, the cron expression 0 */12 * * * schedules the function to run at the start of every 12th hour of any day and any day of the week.
