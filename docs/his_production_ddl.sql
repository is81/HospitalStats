-- ============================================================
-- HIS 生产库表结构 DDL (HIS_DEV 修正用)
-- 依据: DBSCHM.txt + HIS门诊医生站数据结构.txt
-- Schema: HOSPITAL
-- ============================================================

-- ============================================================
-- 1. CLINIC_MASTER (就诊记录) — 重建
-- 来源: DBSCHM.txt §10.5, 行 2026-2061
-- 主键: VISIT_DATE, VISIT_NO, PATIENT_ID
-- ============================================================
DROP TABLE HOSPITAL.CLINIC_MASTER CASCADE CONSTRAINTS;

CREATE TABLE HOSPITAL.CLINIC_MASTER (
    VISIT_DATE              DATE          NOT NULL,
    VISIT_NO                NUMBER(4)     NOT NULL,
    CLINIC_LABEL            VARCHAR2(16),
    VISIT_TIME_DESC         VARCHAR2(8),
    SERIAL_NO               NUMBER(3),
    PATIENT_ID              VARCHAR2(10)  NOT NULL,
    NAME                    VARCHAR2(8),
    NAME_PHONETIC           VARCHAR2(16),
    SEX                     VARCHAR2(4),
    AGE                     NUMBER(3),
    IDENTITY                VARCHAR2(10),
    CHARGE_TYPE             VARCHAR2(8),
    INSURANCE_TYPE          VARCHAR2(16),
    INSURANCE_NO            VARCHAR2(18),
    UNIT_IN_CONTRACT        VARCHAR2(11),
    CLINIC_TYPE             VARCHAR2(8),
    FIRST_VISIT_INDICATOR   NUMBER(1),
    VISIT_DEPT              VARCHAR2(8),
    VISIT_SPECIAL_CLINIC    VARCHAR2(16),
    DOCTOR                  VARCHAR2(8),
    MR_PROVIDED_INDICATOR   NUMBER(1),
    REGISTRATION_STATUS     NUMBER(1),
    REGISTERING_DATE        DATE,
    SYMPTOM                 VARCHAR2(40),
    REGIST_FEE              NUMBER(5,2),
    CLINIC_FEE              NUMBER(5,2),
    OTHER_FEE               NUMBER(5,2),
    CLINIC_CHARGE           NUMBER(5,2),
    OPERATOR                VARCHAR2(8),
    RETURNED_DATE           DATE,
    RETURNED_OPERATOR       VARCHAR2(8),
    CONSTRAINT PK_CLINIC_MASTER PRIMARY KEY (VISIT_DATE, VISIT_NO, PATIENT_ID)
);

-- ============================================================
-- 2. PATS_IN_HOSPITAL (在院病人记录) — 重建
-- 来源: DBSCHM.txt §11.3, 行 2120-2147
-- 主键: PATIENT_ID
-- ============================================================
DROP TABLE HOSPITAL.PATS_IN_HOSPITAL CASCADE CONSTRAINTS;

CREATE TABLE HOSPITAL.PATS_IN_HOSPITAL (
    PATIENT_ID              VARCHAR2(10)  NOT NULL,
    VISIT_ID                NUMBER(2)     NOT NULL,
    WARD_CODE               VARCHAR2(8),
    DEPT_CODE               VARCHAR2(8),
    BED_NO                  NUMBER(3),
    ADMISSION_DATE_TIME     DATE,
    ADM_WARD_DATE_TIME      DATE,
    DIAGNOSIS               VARCHAR2(80),
    PATIENT_CONDITION       VARCHAR2(1),
    NURSING_CLASS           VARCHAR2(1),
    DOCTOR_IN_CHARGE        VARCHAR2(8),
    OPERATING_DATE          DATE,
    BILLING_DATE_TIME       DATE,
    PREPAYMENTS             NUMBER(10,2),
    TOTAL_COSTS             NUMBER(10,2),
    TOTAL_CHARGES           NUMBER(10,2),
    GUARANTOR               VARCHAR2(8),
    GUARANTOR_ORG           VARCHAR2(40),
    GUARANTOR_PHONE_NUM     VARCHAR2(16),
    BILL_CHECKED_DATE_TIME  DATE,
    SETTLED_INDICATOR       NUMBER(1),
    CONSTRAINT PK_PATS_IN_HOSPITAL PRIMARY KEY (PATIENT_ID)
);

-- ============================================================
-- 3. PAT_VISIT (病人住院主记录) — 重建
-- 来源: DBSCHM.txt §9.2, 行 1721-1787
-- 主键: PATIENT_ID, VISIT_ID
-- ============================================================
DROP TABLE HOSPITAL.PAT_VISIT CASCADE CONSTRAINTS;

CREATE TABLE HOSPITAL.PAT_VISIT (
    PATIENT_ID                VARCHAR2(10)  NOT NULL,
    VISIT_ID                  NUMBER(2)     NOT NULL,
    DEPT_ADMISSION_TO         VARCHAR2(8),
    ADMISSION_DATE_TIME       DATE,
    DEPT_DISCHARGE_FROM       VARCHAR2(8),
    DISCHARGE_DATE_TIME       DATE,
    OCCUPATION                VARCHAR2(1),
    MARITAL_STATUS            VARCHAR2(4),
    IDENTITY                  VARCHAR2(10),
    ARMED_SERVICES            VARCHAR2(4),
    DUTY                      VARCHAR2(4),
    UNIT_IN_CONTRACT          VARCHAR2(11),
    CHARGE_TYPE               VARCHAR2(8),
    WORKING_STATUS            NUMBER(1),
    INSURANCE_TYPE            VARCHAR2(16),
    INSURANCE_NO              VARCHAR2(18),
    SERVICE_AGENCY            VARCHAR2(40),
    TOP_UNIT                  VARCHAR2(1),
    SERVICE_SYSTEM_INDICATOR  NUMBER(1),
    MAILING_ADDRESS           VARCHAR2(40),
    ZIP_CODE                  VARCHAR2(6),
    NEXT_OF_KIN               VARCHAR2(8),
    RELATIONSHIP              VARCHAR2(2),
    NEXT_OF_KIN_ADDR          VARCHAR2(40),
    NEXT_OF_KIN_ZIPCODE       VARCHAR2(6),
    NEXT_OF_KIN_PHONE         VARCHAR2(16),
    PATIENT_CLASS             VARCHAR2(1),
    ADMISSION_CAUSE           VARCHAR2(8),
    CONSULTING_DATE           DATE,
    PAT_ADM_CONDITION         VARCHAR2(1),
    CONSULTING_DOCTOR         VARCHAR2(8),
    ADMITTED_BY               VARCHAR2(8),
    EMER_TREAT_TIMES          NUMBER(2),
    ESC_EMER_TIMES            NUMBER(2),
    SERIOUS_COND_DAYS         NUMBER(4),
    CRITICAL_COND_DAYS        NUMBER(4),
    ICU_DAYS                  NUMBER(4),
    CCU_DAYS                  NUMBER(4),
    SPEC_LEVEL_NURS_DAYS      NUMBER(4),
    FIRST_LEVEL_NURS_DAYS     NUMBER(4),
    SECOND_LEVEL_NURS_DAYS    NUMBER(4),
    AUTOPSY_INDICATOR         NUMBER(1),
    BLOOD_TYPE                VARCHAR2(2),
    BLOOD_TYPE_RH             VARCHAR2(1),
    INFUSION_REACT_TIMES      NUMBER(2),
    BLOOD_TRAN_TIMES          NUMBER(2),
    BLOOD_TRAN_VOL            NUMBER(5),
    BLOOD_TRAN_REACT_TIMES    NUMBER(2),
    DECUBITAL_ULCER_TIMES     NUMBER(2),
    ALERGY_DRUGS              VARCHAR2(80),
    ADVERSE_REACTION_DRUGS    VARCHAR2(80),
    MR_VALUE                  VARCHAR2(4),
    MR_QUALITY                VARCHAR2(2),
    FOLLOW_INDICATOR          NUMBER(1),
    FOLLOW_INTERVAL           NUMBER(2),
    FOLLOW_INTERVAL_UNITS     VARCHAR2(2),
    DIRECTOR                  VARCHAR2(8),
    ATTENDING_DOCTOR          VARCHAR2(8),
    DOCTOR_IN_CHARGE          VARCHAR2(8),
    DISCHARGE_DISPOSITION     VARCHAR2(1),
    TOTAL_COSTS               NUMBER(10,2),
    TOTAL_PAYMENTS            NUMBER(10,2),
    CATALOG_DATE              DATE,
    CATALOGER                 VARCHAR2(8),
    CONSTRAINT PK_PAT_VISIT PRIMARY KEY (PATIENT_ID, VISIT_ID)
);

-- ============================================================
-- 4. PAT_MASTER_INDEX (病人主索引) — 重建
-- 来源: DBSCHM.txt §9.1, 行 1690-1720
-- 主键: PATIENT_ID
-- ============================================================
DROP TABLE HOSPITAL.PAT_MASTER_INDEX CASCADE CONSTRAINTS;

CREATE TABLE HOSPITAL.PAT_MASTER_INDEX (
    PATIENT_ID              VARCHAR2(10)  NOT NULL,
    INP_NO                  VARCHAR2(6),
    NAME                    VARCHAR2(8),
    NAME_PHONETIC           VARCHAR2(16),
    SEX                     VARCHAR2(4),
    DATE_OF_BIRTH           DATE,
    BIRTH_PLACE             VARCHAR2(6),
    CITIZENSHIP             VARCHAR2(2),
    NATION                  VARCHAR2(10),
    ID_NO                   VARCHAR2(18),
    IDENTITY                VARCHAR2(10),
    CHARGE_TYPE             VARCHAR2(8),
    UNIT_IN_CONTRACT        VARCHAR2(11),
    MAILING_ADDRESS         VARCHAR2(40),
    ZIP_CODE                VARCHAR2(6),
    PHONE_NUMBER_HOME       VARCHAR2(16),
    PHONE_NUMBER_BUSINESS   VARCHAR2(16),
    NEXT_OF_KIN             VARCHAR2(8),
    RELATIONSHIP            VARCHAR2(2),
    NEXT_OF_KIN_ADDR        VARCHAR2(40),
    NEXT_OF_KIN_ZIP_CODE    VARCHAR2(6),
    NEXT_OF_KIN_PHONE       VARCHAR2(16),
    LAST_VISIT_DATE         DATE,
    VIP_INDICATOR           NUMBER(1),
    CREATE_DATE             DATE,
    OPERATOR                VARCHAR2(8),
    CONSTRAINT PK_PAT_MASTER_INDEX PRIMARY KEY (PATIENT_ID)
);

-- ============================================================
-- 5. INP_BILL_DETAIL (住院病人费用明细) — 追加缺失列
-- 来源: DBSCHM.txt §17.1, 行 3375-3394
-- 现有表基本匹配, 仅缺 OPERATOR_NO, RCPT_NO
-- ============================================================
ALTER TABLE HOSPITAL.INP_BILL_DETAIL ADD (OPERATOR_NO VARCHAR2(4));
ALTER TABLE HOSPITAL.INP_BILL_DETAIL ADD (RCPT_NO VARCHAR2(8));
