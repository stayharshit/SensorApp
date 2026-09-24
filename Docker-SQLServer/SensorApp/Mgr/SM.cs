using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Threading;

namespace SensorApp.Mgr
{
    public class SM
    {
        private static string cs;
        private static string mcs;

        public static void Init(IConfiguration configuration)
        {
            cs = configuration.GetConnectionString("SensorDb")
                ?? throw new InvalidOperationException("ConnectionStrings:SensorDb is required.");
            mcs = configuration.GetConnectionString("MasterDb")
                ?? throw new InvalidOperationException("ConnectionStrings:MasterDb is required.");

            int x = 0;
            Exception lastException = null;
            while (x < 10)
            {
                try
                {
                    var mcon = new SqlConnection(mcs);
                    mcon.Open();
                    new SqlCommand("IF NOT EXISTS (SELECT name FROM sys.databases WHERE name='sdb') CREATE DATABASE sdb", mcon).ExecuteNonQuery();
                    mcon.Close();

                    var con = new SqlConnection(cs);
                    con.Open();

                    new SqlCommand(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='t_dev' AND xtype='U')
CREATE TABLE t_dev (id INT IDENTITY(1,1) PRIMARY KEY, nm VARCHAR(100), loc VARCHAR(200), tp INT, st INT, cfg VARCHAR(500), dt DATETIME)", con).ExecuteNonQuery();

                    new SqlCommand(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='t_dat' AND xtype='U')
CREATE TABLE t_dat (id INT IDENTITY(1,1) PRIMARY KEY, did INT, ts DATETIME, v FLOAT, v2 FLOAT, v3 FLOAT, typ INT, st INT, flg INT, n VARCHAR(500), dt1 DATETIME, dt2 DATETIME)", con).ExecuteNonQuery();

                    new SqlCommand(@"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_t_dat_t_dev')
ALTER TABLE t_dat ADD CONSTRAINT FK_t_dat_t_dev FOREIGN KEY (did) REFERENCES t_dev(id)", con).ExecuteNonQuery();
                    new SqlCommand(@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_t_dat_did_ts' AND object_id=OBJECT_ID('t_dat'))
CREATE INDEX IX_t_dat_did_ts ON t_dat(did, ts)", con).ExecuteNonQuery();

                    new SqlCommand(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='t_log' AND xtype='U')
CREATE TABLE t_log (id INT IDENTITY(1,1) PRIMARY KEY, ref INT, msg VARCHAR(1000), dt DATETIME, flg INT)", con).ExecuteNonQuery();

                    new SqlCommand("IF OBJECT_ID('sp_calc') IS NOT NULL DROP PROCEDURE sp_calc", con).ExecuteNonQuery();

                    new SqlCommand(@"CREATE PROCEDURE sp_calc @did INT AS
BEGIN
    DECLARE @avg FLOAT, @mx FLOAT, @thr FLOAT, @cfg VARCHAR(500), @p INT, @p2 INT
    SELECT @avg = AVG(v), @mx = MAX(v) FROM t_dat WHERE did = @did AND typ = 1 AND ts >= DATEADD(hour,-1,GETDATE())
    SELECT @cfg = cfg FROM t_dev WHERE id = @did
    SET @thr = 75
    IF @cfg IS NOT NULL AND CHARINDEX('thr=',@cfg) > 0
    BEGIN
        SET @p = CHARINDEX('thr=',@cfg) + 4
        SET @p2 = CHARINDEX('|',@cfg,@p)
        IF @p2 = 0 SET @p2 = LEN(@cfg)+1
        SET @thr = CAST(SUBSTRING(@cfg,@p,@p2-@p) AS FLOAT)
    END
    IF @mx > @thr
        INSERT INTO t_dat(did,ts,v,v2,typ,st,flg,n,dt1)
        VALUES(@did,GETDATE(),@mx,@avg,3,1,1,'ALERT: threshold exceeded',GETDATE())
    INSERT INTO t_log(ref,msg,dt,flg) VALUES(@did,'calc did='+CAST(@did AS VARCHAR),GETDATE(),0)
    SELECT @avg avg, @mx mx, @thr thr
END", con).ExecuteNonQuery();

                    new SqlCommand(@"IF NOT EXISTS (SELECT TOP 1 1 FROM t_dev)
BEGIN
    INSERT INTO t_dev(nm,loc,tp,st,cfg,dt) VALUES('snsr-01','Building A|Room 1',1,1,'thr=75|unit=C|int=30',GETDATE())
    INSERT INTO t_dev(nm,loc,tp,st,cfg,dt) VALUES('snsr-02','Building A|Room 2',1,1,'thr=80|unit=C|int=30',GETDATE())
    INSERT INTO t_dev(nm,loc,tp,st,cfg,dt) VALUES('snsr-03','Building B|Floor 1',2,1,'thr=70|unit=F|int=60',GETDATE())
END", con).ExecuteNonQuery();

                    new SqlCommand(@"IF NOT EXISTS (SELECT TOP 1 1 FROM t_dat)
BEGIN
    DECLARE @i INT = 0
    WHILE @i < 100
    BEGIN
        INSERT INTO t_dat(did,ts,v,v2,v3,typ,st,flg,n,dt1) VALUES(1,DATEADD(minute,-@i*5,GETDATE()),65+(@i%15),55+(@i%20),1013+(@i%5),1,1,0,'',GETDATE())
        INSERT INTO t_dat(did,ts,v,v2,v3,typ,st,flg,n,dt1) VALUES(2,DATEADD(minute,-@i*5,GETDATE()),70+(@i%10),60+(@i%15),1010+(@i%8),1,1,0,'',GETDATE())
        SET @i = @i + 1
    END
END", con).ExecuteNonQuery();

                    con.Close();
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    x++;
                    Thread.Sleep(3000);
                }
            }

            throw new InvalidOperationException("Unable to initialize the SQL Server database after several attempts.", lastException);
        }

        public static List<Mdl.D> GetAll(string tp, string did, string df, string dt)
        {
            var r = new List<Mdl.D>();
            try
            {
                using var con = new SqlConnection(cs);
                con.Open();
                string s = "SELECT TOP 1000 * FROM t_dat WHERE 1=1";
                using var cmd = new SqlCommand();
                cmd.Connection = con;
                if (int.TryParse(tp, out var type) && type != 0)
                {
                    s += " AND typ=@typ";
                    cmd.Parameters.Add("@typ", SqlDbType.Int).Value = type;
                }
                if (int.TryParse(did, out var deviceId) && deviceId != 0)
                {
                    s += " AND did=@did";
                    cmd.Parameters.Add("@did", SqlDbType.Int).Value = deviceId;
                }
                if (DateTime.TryParse(df, out var from))
                {
                    s += " AND ts>=@from";
                    cmd.Parameters.Add("@from", SqlDbType.DateTime2).Value = from;
                }
                if (DateTime.TryParse(dt, out var to))
                {
                    s += " AND ts<=@to";
                    cmd.Parameters.Add("@to", SqlDbType.DateTime2).Value = to;
                }
                s += " ORDER BY ts DESC";
                cmd.CommandText = s;
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var d = new Mdl.D();
                    d.Id = Convert.ToInt32(rd["id"]);
                    d.Did = rd["did"] == DBNull.Value ? 0 : Convert.ToInt32(rd["did"]);
                    d.Ts = rd["ts"] == DBNull.Value ? "" : Convert.ToDateTime(rd["ts"]).ToString("yyyy-MM-dd HH:mm:ss");
                    d.V = rd["v"] == DBNull.Value ? 0 : Convert.ToDouble(rd["v"]);
                    d.V2 = rd["v2"] == DBNull.Value ? 0 : Convert.ToDouble(rd["v2"]);
                    d.V3 = rd["v3"] == DBNull.Value ? 0 : Convert.ToDouble(rd["v3"]);
                    d.Typ = rd["typ"] == DBNull.Value ? 0 : Convert.ToInt32(rd["typ"]);
                    d.St = rd["st"] == DBNull.Value ? 0 : Convert.ToInt32(rd["st"]);
                    d.Flg = rd["flg"] == DBNull.Value ? 0 : Convert.ToInt32(rd["flg"]);
                    d.N = rd["n"] == DBNull.Value ? "" : rd["n"].ToString();
                    r.Add(d);
                }
            }
            catch { }
            return r;
        }

        public static bool Save(Mdl.D d)
        {
            if (d == null || d.Did <= 0) return false;

            try
            {
                using var con = new SqlConnection(cs);
                con.Open();
                using var transaction = con.BeginTransaction();

                using (var deviceCheck = new SqlCommand("SELECT COUNT(1) FROM t_dev WHERE id=@did", con, transaction))
                {
                    deviceCheck.Parameters.Add("@did", SqlDbType.Int).Value = d.Did;
                    if (Convert.ToInt32(deviceCheck.ExecuteScalar()) == 0) return false;
                }

                using (var cmd = new SqlCommand(@"INSERT INTO t_dat(did,ts,v,v2,v3,typ,st,flg,n,dt1)
                    VALUES(@did,@ts,@v,@v2,@v3,@typ,@st,@flg,@note,GETDATE())", con, transaction))
                {
                    cmd.Parameters.Add("@did", SqlDbType.Int).Value = d.Did;
                    cmd.Parameters.Add("@ts", SqlDbType.DateTime2).Value = ParseTimestamp(d.Ts);
                    cmd.Parameters.Add("@v", SqlDbType.Float).Value = d.V;
                    cmd.Parameters.Add("@v2", SqlDbType.Float).Value = d.V2;
                    cmd.Parameters.Add("@v3", SqlDbType.Float).Value = d.V3;
                    cmd.Parameters.Add("@typ", SqlDbType.Int).Value = d.Typ;
                    cmd.Parameters.Add("@st", SqlDbType.Int).Value = d.St;
                    cmd.Parameters.Add("@flg", SqlDbType.Int).Value = d.Flg;
                    cmd.Parameters.Add("@note", SqlDbType.VarChar, 500).Value = d.N ?? "";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SqlCommand("UPDATE t_dev SET dt=GETDATE() WHERE id=@did", con, transaction))
                {
                    cmd.Parameters.Add("@did", SqlDbType.Int).Value = d.Did;
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SqlCommand("INSERT INTO t_log(ref,msg,dt,flg) VALUES(@did,'data saved',GETDATE(),0)", con, transaction))
                {
                    cmd.Parameters.Add("@did", SqlDbType.Int).Value = d.Did;
                    cmd.ExecuteNonQuery();
                }

                string cfg = "";
                using var cfgCommand = new SqlCommand("SELECT cfg FROM t_dev WHERE id=@did", con, transaction);
                cfgCommand.Parameters.Add("@did", SqlDbType.Int).Value = d.Did;
                using (var rd2 = cfgCommand.ExecuteReader())
                {
                    if (rd2.Read()) cfg = rd2["cfg"] == DBNull.Value ? "" : rd2["cfg"].ToString();
                }

                double thr = 75;
                if (cfg.Contains("thr="))
                {
                    int p = cfg.IndexOf("thr=") + 4;
                    int p2 = cfg.IndexOf("|", p);
                    if (p2 < 0) p2 = cfg.Length;
                    if (double.TryParse(cfg.Substring(p, p2 - p), out var configuredThreshold))
                        thr = configuredThreshold;
                }
                if (d.V > thr)
                {
                    using var alertCommand = new SqlCommand(@"INSERT INTO t_dat(did,ts,v,v2,typ,st,flg,n,dt1)
                        VALUES(@did,GETDATE(),@value,@threshold,3,1,1,'AUTO ALERT',GETDATE())", con, transaction);
                    alertCommand.Parameters.Add("@did", SqlDbType.Int).Value = d.Did;
                    alertCommand.Parameters.Add("@value", SqlDbType.Float).Value = d.V;
                    alertCommand.Parameters.Add("@threshold", SqlDbType.Float).Value = thr;
                    alertCommand.ExecuteNonQuery();

                    using var alertLogCommand = new SqlCommand("INSERT INTO t_log(ref,msg,dt,flg) VALUES(@did,@message,GETDATE(),1)", con, transaction);
                    alertLogCommand.Parameters.Add("@did", SqlDbType.Int).Value = d.Did;
                    alertLogCommand.Parameters.Add("@message", SqlDbType.VarChar, 1000).Value = $"alert val={d.V}";
                    alertLogCommand.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch { return false; }
        }

        private static DateTime ParseTimestamp(string timestamp)
        {
            return DateTime.TryParse(timestamp, out var parsed) ? parsed : DateTime.Now;
        }

        public static List<Mdl.D> GetDevs(string st)
        {
            var r = new List<Mdl.D>();
            try
            {
                using var con = new SqlConnection(cs);
                con.Open();
                string s = "SELECT * FROM t_dev";
                using var cmd = new SqlCommand();
                cmd.Connection = con;
                if (int.TryParse(st, out var state) && state != 0)
                {
                    s += " WHERE st=@state";
                    cmd.Parameters.Add("@state", SqlDbType.Int).Value = state;
                }
                cmd.CommandText = s;
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var d = new Mdl.D();
                    d.Id = Convert.ToInt32(rd["id"]);
                    d.Nm = rd["nm"] == DBNull.Value ? "" : rd["nm"].ToString();
                    d.Loc = rd["loc"] == DBNull.Value ? "" : rd["loc"].ToString();
                    d.Tp = rd["tp"] == DBNull.Value ? 0 : Convert.ToInt32(rd["tp"]);
                    d.St = rd["st"] == DBNull.Value ? 0 : Convert.ToInt32(rd["st"]);
                    d.Cfg = rd["cfg"] == DBNull.Value ? "" : rd["cfg"].ToString();
                    d.Ts = rd["dt"] == DBNull.Value ? "" : Convert.ToDateTime(rd["dt"]).ToString("yyyy-MM-dd HH:mm:ss");
                    if (!string.IsNullOrEmpty(d.Cfg))
                    {
                        try
                        {
                            foreach (var kv in d.Cfg.Split('|'))
                            {
                                var tmp = kv.Split('=');
                                if (tmp.Length == 2)
                                {
                                    if (tmp[0] == "thr" && double.TryParse(tmp[1], out var threshold)) d.V = threshold;
                                    if (tmp[0] == "int" && double.TryParse(tmp[1], out var interval)) d.V2 = interval;
                                }
                            }
                        }
                        catch { }
                    }
                    r.Add(d);
                }
            }
            catch { }
            return r;
        }

        public static bool SaveDev(Mdl.D d)
        {
            if (d == null) return false;

            try
            {
                using var con = new SqlConnection(cs);
                con.Open();
                using var transaction = con.BeginTransaction();
                if (d.Id > 0)
                {
                    using var update = new SqlCommand(@"UPDATE t_dev SET nm=@name,loc=@location,tp=@type,st=@state,cfg=@config,dt=GETDATE()
                        WHERE id=@id", con, transaction);
                    update.Parameters.Add("@id", SqlDbType.Int).Value = d.Id;
                    AddDeviceParameters(update, d);
                    if (update.ExecuteNonQuery() == 0) return false;

                    using var log = new SqlCommand("INSERT INTO t_log(ref,msg,dt,flg) VALUES(@id,'dev updated',GETDATE(),0)", con, transaction);
                    log.Parameters.Add("@id", SqlDbType.Int).Value = d.Id;
                    log.ExecuteNonQuery();
                }
                else
                {
                    using var insert = new SqlCommand("INSERT INTO t_dev(nm,loc,tp,st,cfg,dt) VALUES(@name,@location,@type,@state,@config,GETDATE())", con, transaction);
                    AddDeviceParameters(insert, d);
                    insert.ExecuteNonQuery();

                    using var log = new SqlCommand("INSERT INTO t_log(ref,msg,dt,flg) VALUES(0,@message,GETDATE(),0)", con, transaction);
                    log.Parameters.Add("@message", SqlDbType.VarChar, 1000).Value = $"dev added {d.Nm}";
                    log.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch { return false; }
        }

        private static void AddDeviceParameters(SqlCommand command, Mdl.D device)
        {
            command.Parameters.Add("@name", SqlDbType.VarChar, 100).Value = device.Nm ?? "";
            command.Parameters.Add("@location", SqlDbType.VarChar, 200).Value = device.Loc ?? "";
            command.Parameters.Add("@type", SqlDbType.Int).Value = device.Tp;
            command.Parameters.Add("@state", SqlDbType.Int).Value = device.St;
            command.Parameters.Add("@config", SqlDbType.VarChar, 500).Value = device.Cfg ?? "";
        }

        public static object Calc(int did)
        {
            try
            {
                using var con = new SqlConnection(cs);
                con.Open();
                using var cmd = new SqlCommand("sp_calc", con);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add("@did", SqlDbType.Int).Value = did;
                using var rd = cmd.ExecuteReader();
                if (rd.Read())
                    return new
                    {
                        avg = rd["avg"] == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(rd["avg"]), 2),
                        mx = rd["mx"] == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(rd["mx"]), 2),
                        thr = rd["thr"]
                    };
            }
            catch { }
                    return null;
        }

        public static List<Mdl.D> GetLog(string did, string flg)
        {
            var r = new List<Mdl.D>();
            try
            {
                using var con = new SqlConnection(cs);
                con.Open();
                string s = "SELECT * FROM t_log WHERE 1=1";
                using var cmd = new SqlCommand();
                cmd.Connection = con;
                if (int.TryParse(did, out var deviceId) && deviceId != 0)
                {
                    s += " AND ref=@did";
                    cmd.Parameters.Add("@did", SqlDbType.Int).Value = deviceId;
                }
                if (int.TryParse(flg, out var flag) && flag != -1)
                {
                    s += " AND flg=@flag";
                    cmd.Parameters.Add("@flag", SqlDbType.Int).Value = flag;
                }
                s += " ORDER BY dt DESC";
                cmd.CommandText = s;
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var d = new Mdl.D();
                    d.Id = Convert.ToInt32(rd["id"]);
                    d.Did = rd["ref"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ref"]);
                    d.N = rd["msg"] == DBNull.Value ? "" : rd["msg"].ToString();
                    d.Ts = rd["dt"] == DBNull.Value ? "" : Convert.ToDateTime(rd["dt"]).ToString("yyyy-MM-dd HH:mm:ss");
                    d.Flg = rd["flg"] == DBNull.Value ? 0 : Convert.ToInt32(rd["flg"]);
                    r.Add(d);
                }
            }
            catch { }
            return r;
        }

        public static object Stats(int did)
        {
            try
            {
                using var con = new SqlConnection(cs);
                con.Open();
                string s = "SELECT COUNT(*) total, AVG(v) avg_v, MAX(v) max_v, MIN(v) min_v, " +
                    "SUM(CASE WHEN typ=3 THEN 1 ELSE 0 END) alerts, SUM(CASE WHEN typ=1 THEN 1 ELSE 0 END) readings, MAX(ts) last_ts " +
                    "FROM t_dat WHERE did=@did AND typ IN (1,3)";
                using var cmd = new SqlCommand(s, con);
                cmd.Parameters.Add("@did", SqlDbType.Int).Value = did;
                using var rd = cmd.ExecuteReader();
                if (rd.Read())
                {
                    return new
                    {
                        total = rd["total"],
                        avg = rd["avg_v"] == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(rd["avg_v"]), 2),
                        max = rd["max_v"],
                        min = rd["min_v"],
                        alerts = rd["alerts"],
                        readings = rd["readings"],
                        last = rd["last_ts"] == DBNull.Value ? "" : Convert.ToDateTime(rd["last_ts"]).ToString("yyyy-MM-dd HH:mm:ss")
                    };
                }
            }
            catch { }
            return null;
        }
    }
}
